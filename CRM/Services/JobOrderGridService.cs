using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CRM.Models;
using Microsoft.Extensions.Logging;

namespace CRM.Services
{
    public class JobOrderGridService : IJobOrderGridService
    {
        private const string JoCardTable = "JOBCARD_STAGE_STATUS";

        private readonly IMasterDataService _masterDataService;
        private readonly ILogger<JobOrderGridService> _logger;

        public JobOrderGridService(IMasterDataService masterDataService, ILogger<JobOrderGridService> logger)
        {
            _masterDataService = masterDataService;
            _logger = logger;
        }

        public void InvalidateCache()
        {
            _masterDataService.InvalidateCache(JoCardTable);
        }

        public async Task<JobOrderFilterOptions> GetFilterOptionsAsync(string item = "", string category = "", string size = "")
        {
            var options = new JobOrderFilterOptions();
            var master = await _masterDataService.GetMasterDataAsync(JoCardTable);

            if (!string.IsNullOrEmpty(master.ErrorMessage))
            {
                return options;
            }

            string itemCol = FindColumn(master.ColumnNames, "Name");
            string catCol = FindColumn(master.ColumnNames, "Cat1");
            string sizeCol = FindColumn(master.ColumnNames, "SizeName");

            item = (item ?? string.Empty).Trim();
            category = (category ?? string.Empty).Trim();
            size = (size ?? string.Empty).Trim();

            var items = new SortedDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var categories = new SortedDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var sizes = new SortedDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            // Fully interdependent: each dropdown only lists values that still
            // satisfy the current selections of the other two dropdowns.

            // Items honored when category + size selections match
            foreach (var row in master.Rows)
            {
                if (catCol.Length > 0 && category.Length > 0 &&
                    !string.Equals(GetValue(row, catCol), category, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (sizeCol.Length > 0 && size.Length > 0 &&
                    !string.Equals(GetValue(row, sizeCol), size, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (itemCol.Length > 0)
                {
                    var v = GetValue(row, itemCol);
                    if (v.Length > 0) items[v] = true;
                }
            }

            // Categories honored when item + size selections match
            foreach (var row in master.Rows)
            {
                if (itemCol.Length > 0 && item.Length > 0 &&
                    !string.Equals(GetValue(row, itemCol), item, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (sizeCol.Length > 0 && size.Length > 0 &&
                    !string.Equals(GetValue(row, sizeCol), size, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (catCol.Length > 0)
                {
                    var v = GetValue(row, catCol);
                    if (v.Length > 0) categories[v] = true;
                }
            }

            // Sizes honored when item + category selections match
            foreach (var row in master.Rows)
            {
                if (itemCol.Length > 0 && item.Length > 0 &&
                    !string.Equals(GetValue(row, itemCol), item, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (catCol.Length > 0 && category.Length > 0 &&
                    !string.Equals(GetValue(row, catCol), category, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (sizeCol.Length > 0)
                {
                    var v = GetValue(row, sizeCol);
                    if (v.Length > 0) sizes[v] = true;
                }
            }

            options.Items = items.Keys.ToList();
            options.Categories = categories.Keys.ToList();
            options.Sizes = sizes.Keys.ToList();
            return options;
        }

        public async Task<JobOrderPageResult> GetPageAsync(
            int page,
            int pageSize,
            string search,
            string sortColumn,
            string sortDir,
            string fromDate = "",
            string toDate = "",
            string item = "",
            string category = "",
            string size = "")
        {
            var master = await _masterDataService.GetMasterDataAsync(JoCardTable);

            if (!string.IsNullOrEmpty(master.ErrorMessage))
            {
                return new JobOrderPageResult { ErrorMessage = master.ErrorMessage };
            }

            var columns = master.ColumnNames;
            var allRows = master.Rows;

            var result = new JobOrderPageResult
            {
                ColumnNames = columns
            };

            try
            {
                string dateCol = FindColumn(columns, "Date");
                string itemCol = FindColumn(columns, "Name");
                string catCol = FindColumn(columns, "Cat1");
                string sizeCol = FindColumn(columns, "SizeName");
                fromDate = (fromDate ?? string.Empty).Trim();
                toDate = (toDate ?? string.Empty).Trim();
                item = (item ?? string.Empty).Trim();
                category = (category ?? string.Empty).Trim();
                size = (size ?? string.Empty).Trim();

                IEnumerable<Dictionary<string, object?>> filtered = allRows;
                if (dateCol.Length > 0 && fromDate.Length > 0)
                {
                    string f = fromDate;
                    filtered = filtered.Where(r => string.CompareOrdinal(GetValue(r, dateCol), f) >= 0);
                }
                if (dateCol.Length > 0 && toDate.Length > 0)
                {
                    string t = toDate;
                    filtered = filtered.Where(r => string.CompareOrdinal(GetValue(r, dateCol), t) <= 0);
                }
                if (itemCol.Length > 0 && item.Length > 0)
                {
                    string c = item;
                    filtered = filtered.Where(r => string.Equals(GetValue(r, itemCol), c, StringComparison.OrdinalIgnoreCase));
                }
                if (catCol.Length > 0 && category.Length > 0)
                {
                    string c = category;
                    filtered = filtered.Where(r => string.Equals(GetValue(r, catCol), c, StringComparison.OrdinalIgnoreCase));
                }
                if (sizeCol.Length > 0 && size.Length > 0)
                {
                    string c = size;
                    filtered = filtered.Where(r => string.Equals(GetValue(r, sizeCol), c, StringComparison.OrdinalIgnoreCase));
                }

                var baseRows = filtered.ToList();
                ComputeStats(result, columns, baseRows);

                var query = (search ?? string.Empty).Trim();
                var queryFiltered = baseRows.AsEnumerable();

                if (query.Length > 0)
                {
                    queryFiltered = queryFiltered.Where(r => columns.Any(c =>
                    {
                        var v = GetValue(r, c);
                        return v.Length > 0 && v.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                    }));
                }

                var filteredList = queryFiltered.ToList();

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

                result.Total = filteredList.Count;
                result.TotalPages = pageSize > 0 ? (int)Math.Ceiling(result.Total / (double)pageSize) : 1;

                if (pageSize > 0)
                {
                    int skip = Math.Max((page - 1) * pageSize, 0);
                    result.Rows = filteredList.Skip(skip).Take(pageSize).ToList();
                }
                else
                {
                    result.Rows = filteredList;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing Job Order grid data.");
                result.ErrorMessage = $"Data processing error: {ex.Message}";
            }

            return result;
        }

        private static void ComputeStats(JobOrderPageResult result, List<string> columns, List<Dictionary<string, object?>> rows)
        {
            string docNoCol = FindColumn(columns, "Docno");
            string jobQtyCol = FindColumn(columns, "JobQty");
            string cutQtyCol = FindColumn(columns, "CutQty");
            string balCutCol = FindColumn(columns, "BalCut");

            var docNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            decimal jobQty = 0m;
            decimal cutQty = 0m;
            decimal balCut = 0m;

            foreach (var row in rows)
            {
                if (docNoCol.Length > 0)
                {
                    var v = GetValue(row, docNoCol);
                    if (v.Length > 0) docNos.Add(v);
                }
                if (jobQtyCol.Length > 0 && TryParseDecimal(GetValue(row, jobQtyCol), out var jq))
                {
                    jobQty += jq;
                }
                if (cutQtyCol.Length > 0 && TryParseDecimal(GetValue(row, cutQtyCol), out var cq))
                {
                    cutQty += cq;
                }
                if (balCutCol.Length > 0 && TryParseDecimal(GetValue(row, balCutCol), out var bc))
                {
                    balCut += bc;
                }
            }

            result.TotalJobs = docNos.Count;
            result.JobQty = jobQty;
            result.CutQty = cutQty;
            result.BalCut = balCut;
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