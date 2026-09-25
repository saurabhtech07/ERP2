using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CRM.Models;
using Microsoft.Extensions.Logging;

namespace CRM.Services
{
    public class MasterGridService : IMasterGridService
    {
        private const string SalesOrderReportTable = "SALES_ORDER_REPORT";

        private static readonly Regex StatusColumnRegex = new Regex("status|active|isactive", RegexOptions.IgnoreCase);
        private static readonly Regex GstColumnRegex = new Regex("gst", RegexOptions.IgnoreCase);

        private readonly IMasterDataService _masterDataService;
        private readonly ILogger<MasterGridService> _logger;

        public MasterGridService(IMasterDataService masterDataService, ILogger<MasterGridService> logger)
        {
            _masterDataService = masterDataService;
            _logger = logger;
        }

        public void InvalidateCache(string tableName)
        {
            _masterDataService.InvalidateCache(tableName);
        }

        public async Task<SalesFilterOptions> GetSalesFilterOptionsAsync(string client = "", string salesman = "", string status = "")
        {
            var options = new SalesFilterOptions();
            var master = await _masterDataService.GetMasterDataAsync(SalesOrderReportTable);

            if (!string.IsNullOrEmpty(master.ErrorMessage))
            {
                return options;
            }

            string clientCol = FindColumn(master.ColumnNames, "Client");
            string salesmanCol = FindColumn(master.ColumnNames, "SalesMan");
            string closeynCol = FindColumn(master.ColumnNames, "Closeyn");

            client = (client ?? string.Empty).Trim();
            salesman = (salesman ?? string.Empty).Trim();
            status = (status ?? string.Empty).Trim();

            bool wantOpen = true;
            bool wantClosed = true;
            if (string.Equals(status, "open", StringComparison.OrdinalIgnoreCase))
            {
                wantClosed = false;
            }
            else if (string.Equals(status, "closed", StringComparison.OrdinalIgnoreCase))
            {
                wantOpen = false;
            }

            var clients = new SortedDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var salesmen = new SortedDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var statuses = new SortedDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            // Fully interdependent: each dropdown only lists values that still
            // satisfy the current selections of the other two dropdowns.

            // Clients honored when salesman + status selections match
            foreach (var row in master.Rows)
            {
                if (salesmanCol.Length > 0 && salesman.Length > 0 &&
                    !string.Equals(GetValue(row, salesmanCol), salesman, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (closeynCol.Length > 0 &&
                    !MatchesCloseStatus(GetValue(row, closeynCol), wantOpen, wantClosed))
                {
                    continue;
                }
                if (clientCol.Length > 0)
                {
                    var v = GetValue(row, clientCol);
                    if (v.Length > 0) clients[v] = true;
                }
            }

            // Salesmen honored when client + status selections match
            foreach (var row in master.Rows)
            {
                if (clientCol.Length > 0 && client.Length > 0 &&
                    !string.Equals(GetValue(row, clientCol), client, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (closeynCol.Length > 0 &&
                    !MatchesCloseStatus(GetValue(row, closeynCol), wantOpen, wantClosed))
                {
                    continue;
                }
                if (salesmanCol.Length > 0)
                {
                    var v = GetValue(row, salesmanCol);
                    if (v.Length > 0) salesmen[v] = true;
                }
            }

            // Statuses honored when client + salesman selections match
            foreach (var row in master.Rows)
            {
                if (clientCol.Length > 0 && client.Length > 0 &&
                    !string.Equals(GetValue(row, clientCol), client, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (salesmanCol.Length > 0 && salesman.Length > 0 &&
                    !string.Equals(GetValue(row, salesmanCol), salesman, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (closeynCol.Length > 0)
                {
                    var cy = GetValue(row, closeynCol);
                    if (string.Equals(cy, "N", StringComparison.OrdinalIgnoreCase)) statuses["Open"] = true;
                    else if (string.Equals(cy, "Y", StringComparison.OrdinalIgnoreCase)) statuses["Closed"] = true;
                }
            }

            options.Clients = clients.Keys.ToList();
            options.Salesmen = salesmen.Keys.ToList();
            options.Statuses = statuses.Keys.ToList();
            return options;
        }

        private static bool MatchesCloseStatus(string closeynValue, bool wantOpen, bool wantClosed)
        {
            bool isOpen = string.Equals(closeynValue, "N", StringComparison.OrdinalIgnoreCase);
            bool isClosed = string.Equals(closeynValue, "Y", StringComparison.OrdinalIgnoreCase);
            return (wantOpen && isOpen) || (wantClosed && isClosed);
        }

        public async Task<MasterDataPageResult> GetPageAsync(
            string tableName,
            int page,
            int pageSize,
            string search,
            string sortColumn,
            string sortDir,
            string statusFilter,
            string fromDate = "",
            string toDate = "",
            string client = "",
            string salesman = "",
            string salesStatus = "all")
        {
            var master = await _masterDataService.GetMasterDataAsync(tableName);

            if (!string.IsNullOrEmpty(master.ErrorMessage))
            {
                return new MasterDataPageResult { ErrorMessage = master.ErrorMessage };
            }

            var columns = master.ColumnNames;
            var allRows = master.Rows;

            int statusIdx = columns.FindIndex(c => StatusColumnRegex.IsMatch(c));
            int gstIdx = columns.FindIndex(c => GstColumnRegex.IsMatch(c));

            var result = new MasterDataPageResult
            {
                ColumnNames = columns,
                ActiveCount = CountByStatus(allRows, columns, statusIdx, true),
                InactiveCount = CountByStatus(allRows, columns, statusIdx, false),
                GstCount = CountGst(allRows, columns, gstIdx)
            };

            try
            {
                List<Dictionary<string, object?>> baseRows;

                if (string.Equals(tableName, SalesOrderReportTable, StringComparison.OrdinalIgnoreCase))
                {
                    baseRows = ApplySalesFilters(allRows, columns, fromDate, toDate, client, salesman, salesStatus);
                    ComputeSalesStats(result, columns, baseRows);
                }
                else
                {
                    baseRows = allRows;
                    if (statusIdx >= 0 && !string.Equals(statusFilter, "all", StringComparison.OrdinalIgnoreCase))
                    {
                        bool wantActive = string.Equals(statusFilter, "active", StringComparison.OrdinalIgnoreCase);
                        baseRows = baseRows.Where(r => MatchesStatus(r, columns[statusIdx], wantActive)).ToList();
                    }
                }

                // Search filter
                var query = (search ?? string.Empty).Trim();
                var filtered = baseRows.AsEnumerable();
                if (query.Length > 0)
                {
                    filtered = filtered.Where(r => columns.Any(c =>
                    {
                        var v = GetValue(r, c);
                        return v.Length > 0 && v.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                    }));
                }

                var filteredList = filtered.ToList();

                // Sort
                if (!string.IsNullOrEmpty(sortColumn))
                {
                    int sortIdx = columns.FindIndex(c => string.Equals(c, sortColumn, StringComparison.OrdinalIgnoreCase));
                    if (sortIdx >= 0)
                    {
                        bool asc = !string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
                        filteredList.Sort((a, b) =>
                        {
                            int cmp = CompareCells(GetValue(a, sortColumn), GetValue(b, sortColumn));
                            return asc ? cmp : -cmp;
                        });
                    }
                }

                // Pagination
                result.Total = filteredList.Count;
                result.TotalPages = pageSize > 0 ? (int)Math.Ceiling(result.Total / (double)pageSize) : 1;

                if (pageSize > 0)
                {
                    int skip = Math.Max((page - 1) * pageSize, 0);
                    result.Rows = filteredList.Skip(skip).Take(pageSize).ToList();
                }
                else
                {
                    // pageSize == 0  =>  return all filtered rows (used for export/print)
                    result.Rows = filteredList;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing grid data for {TableName}", tableName);
                result.ErrorMessage = $"Data processing error: {ex.Message}";
            }

            return result;
        }

        private static string GetValue(Dictionary<string, object?> row, string column)
        {
            if (row.TryGetValue(column, out var val) && val != null)
            {
                return Convert.ToString(val, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            }
            return string.Empty;
        }

        private static int CompareCells(string a, string b)
        {
            bool numA = double.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out double da);
            bool numB = double.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out double db);
            if (numA && numB) return da.CompareTo(db);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesStatus(Dictionary<string, object?> row, string statusColumn, bool wantActive)
        {
            var v = GetValue(row, statusColumn).ToLowerInvariant();
            bool active = v == "1" || v == "true" || v.Contains("active");
            bool inactive = v == "0" || v == "false" || v.Contains("inactive");
            return wantActive ? active : inactive;
        }

        private static int CountByStatus(List<Dictionary<string, object?>> rows, List<string> columns, int statusIdx, bool active)
        {
            if (statusIdx < 0) return 0;
            var col = columns[statusIdx];
            return rows.Count(r => MatchesStatus(r, col, active));
        }

        private static int CountGst(List<Dictionary<string, object?>> rows, List<string> columns, int gstIdx)
        {
            if (gstIdx < 0) return 0;
            var col = columns[gstIdx];
            return rows.Count(r =>
            {
                var v = GetValue(r, col);
                return v.Length > 2;
            });
        }

        private static void ComputeSalesStats(MasterDataPageResult result, List<string> columns, List<Dictionary<string, object?>> rows)
        {
            string docNoCol = FindColumn(columns, "DocNo");
            string closeynCol = FindColumn(columns, "Closeyn");
            string pendQtyCol = FindColumn(columns, "PendQty");
            string invQtyCol = FindColumn(columns, "InvQty");

            var docNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var openDocNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            decimal pendQty = 0m;
            decimal invoicedQty = 0m;

            foreach (var row in rows)
            {
                if (docNoCol.Length > 0)
                {
                    string docNo = GetValue(row, docNoCol);
                    if (docNo.Length > 0)
                    {
                        docNos.Add(docNo);
                        if (closeynCol.Length > 0 &&
                            string.Equals(GetValue(row, closeynCol), "N", StringComparison.OrdinalIgnoreCase))
                        {
                            openDocNos.Add(docNo);
                        }
                    }
                }

                if (pendQtyCol.Length > 0)
                {
                    if (TryParseDecimal(GetValue(row, pendQtyCol), out var pq)) pendQty += pq;
                }

                if (invQtyCol.Length > 0)
                {
                    if (TryParseDecimal(GetValue(row, invQtyCol), out var iq)) invoicedQty += iq;
                }
            }

            result.TotalOrders = docNos.Count;
            result.OpenOrders = openDocNos.Count;
            result.PendingQty = pendQty;
            result.InvoicedQty = invoicedQty;
        }

        private static List<Dictionary<string, object?>> ApplySalesFilters(
            List<Dictionary<string, object?>> rows,
            List<string> columns,
            string fromDate,
            string toDate,
            string client,
            string salesman,
            string salesStatus)
        {
            string dateCol = FindColumn(columns, "DocDate");
            string clientCol = FindColumn(columns, "Client");
            string salesmanCol = FindColumn(columns, "SalesMan");
            string closeynCol = FindColumn(columns, "Closeyn");

            fromDate = (fromDate ?? string.Empty).Trim();
            toDate = (toDate ?? string.Empty).Trim();
            client = (client ?? string.Empty).Trim();
            salesman = (salesman ?? string.Empty).Trim();

            IEnumerable<Dictionary<string, object?>> query = rows;

            if (dateCol.Length > 0 && fromDate.Length > 0)
            {
                string f = fromDate;
                query = query.Where(r => string.CompareOrdinal(GetValue(r, dateCol), f) >= 0);
            }
            if (dateCol.Length > 0 && toDate.Length > 0)
            {
                string t = toDate;
                query = query.Where(r => string.CompareOrdinal(GetValue(r, dateCol), t) <= 0);
            }
            if (clientCol.Length > 0 && client.Length > 0)
            {
                string c = client;
                query = query.Where(r => string.Equals(GetValue(r, clientCol), c, StringComparison.OrdinalIgnoreCase));
            }
            if (salesmanCol.Length > 0 && salesman.Length > 0)
            {
                string s = salesman;
                query = query.Where(r => string.Equals(GetValue(r, salesmanCol), s, StringComparison.OrdinalIgnoreCase));
            }
            if (closeynCol.Length > 0 && !string.Equals(salesStatus, "all", StringComparison.OrdinalIgnoreCase))
            {
                bool open = string.Equals(salesStatus, "open", StringComparison.OrdinalIgnoreCase);
                string expected = open ? "N" : "Y";
                query = query.Where(r => string.Equals(GetValue(r, closeynCol), expected, StringComparison.OrdinalIgnoreCase));
            }

            return query.ToList();
        }

        private static string FindColumn(List<string> columns, string name)
        {
            return columns.FirstOrDefault(c => string.Equals(c.Trim(), name, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        }

        private static bool TryParseDecimal(string value, out decimal parsed)
        {
            return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed);
        }
    }
}