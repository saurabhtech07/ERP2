using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CRM.Data;
using CRM.Models;
using System.Text.Json;

namespace CRM.Controllers
{
    [Authorize]
    public class DynamicReportController : Controller
    {
        private readonly DataAccess _data;

        public DynamicReportController(IConfiguration configuration)
        {
            _data = new DataAccess(configuration.GetConnectionString("DefaultConnection")!);
        }

        public async Task<IActionResult> Index(string tableName)
        {
            // Get dynamic report list from SP
            var menuItems = await _data.GetWebTblMasterAsync();
            var reportName = menuItems.FirstOrDefault(x => x.Tbl_View_Name == tableName)?.Report_Name ?? "Report";

            ViewBag.Title = reportName;
            ViewBag.pagetitle = "Reports";
            ViewBag.ptitle = reportName;
            ViewBag.SelectedTable = tableName;
            ViewBag.ReportName = reportName;
            ViewBag.MenuItems = menuItems;

            // Empty grid state since no report is selected yet
            ViewBag.ColumnNames = new List<string>();
            ViewBag.ItemsData = new List<object>();
            ViewBag.ErrorMessage = "";

            return View(menuItems);
        }

        [HttpGet]
        public async Task<JsonResult> GetReportData(string tableName)
        {
            try
            {
                var data = await _data.GetMasterDataAsync(tableName);
                var menuItems = await _data.GetWebTblMasterAsync();
                var menuItem = menuItems.FirstOrDefault(x => x.Tbl_View_Name == tableName);
                var reportName = menuItem?.Report_Name ?? string.Empty;
                var filterFields = menuItem?.FilterFields
                    .Select(x => (object)new { field = x.FieldName, visible = x.Visible })
                    .ToList() ?? new List<object>();

                var cardFields = menuItem?.CardFields
                    .Select(x => (object)new { slot = x.Slot, field = x.FieldName, label = x.Label })
                    .ToList() ?? new List<object>();

                // Read-only reports still get an ACTIONS column (View only), so the
                // grid needs a column to identify a row by. Editable masters declare
                // their own key; everything else falls back to a best-effort match.
                var keyColumn = menuItem?.KeyColumn;
                if (string.IsNullOrWhiteSpace(keyColumn))
                    keyColumn = ResolveRowKeyColumn(data.SourceColumnNames, data.ColumnNames);

                var dateFilterColumn = ResolveDateFilterColumn(
                    menuItem?.FilterFields, data.SourceColumnNames, data.ColumnNames, data.ColumnTypes);

                return Json(new
                {
                    success = data.ErrorMessage == null,
                    message = data.ErrorMessage,
                    tableName = tableName,
                    reportName = reportName,
                    reportType = menuItem?.Report_Type ?? string.Empty,
                    isEditable = menuItem?.IsEditable ?? false,
                    keyColumn = keyColumn,
                    displayColumn = menuItem?.DisplayColumn ?? "Name",
                    dateFilterColumn = dateFilterColumn,
                    columns = data.ColumnNames,
                    sourceColumns = data.SourceColumnNames,
                    columnTypes = data.ColumnTypes,
                    rows = data.Rows,
                    filterFields = filterFields,
                    cardFields = cardFields
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Picks the column the From/To range filter runs on. A config field from
        /// Web_tbl_master counts only when the SP result set really has a column
        /// with that exact name AND the SP reports its type as a date. No alias,
        /// no substring match, no "grab whichever column looks like a date"
        /// fallback - that is what used to open the filter on Item_Raw, whose
        /// only date column is the audit field Modify_Date while the config says
        /// "Date". Returns null when nothing matches, and the filter stays hidden.
        /// </summary>
        private static string? ResolveDateFilterColumn(
            List<ReportFilterField>? filterFields,
            List<string>? sourceColumns,
            List<string>? columns,
            List<string>? columnTypes)
        {
            if (filterFields == null || sourceColumns == null ||
                columns == null || columnTypes == null)
            {
                return null;
            }

            foreach (var field in filterFields)
            {
                if (!field.Visible) continue;

                var wanted = (field.FieldName ?? string.Empty).Trim();
                if (wanted.Length == 0) continue;

                for (int i = 0; i < columns.Count && i < sourceColumns.Count && i < columnTypes.Count; i++)
                {
                    if (!string.Equals(sourceColumns[i]?.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (columnTypes[i] != null &&
                        columnTypes[i].Contains("Date", StringComparison.OrdinalIgnoreCase))
                    {
                        return columns[i];
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Picks the column used to label a row in the ACTIONS column. Prefers a real
        /// key (Id, then any *Id), then a code column, and finally the first column -
        /// so every report can show the column even when it is read-only.
        /// </summary>
        private static string ResolveRowKeyColumn(
            List<string>? sourceColumns,
            List<string>? columns)
        {
            if (columns == null || columns.Count == 0)
                return string.Empty;

            string? FirstMatch(Func<string, bool> test)
            {
                for (int i = 0; i < columns.Count; i++)
                {
                    var name = sourceColumns != null && i < sourceColumns.Count &&
                               !string.IsNullOrWhiteSpace(sourceColumns[i])
                        ? sourceColumns[i]
                        : columns[i];
                    if (test(name)) return columns[i];
                }
                return null;
            }

            return FirstMatch(n => n.Equals("Id", StringComparison.OrdinalIgnoreCase))
                ?? FirstMatch(n => n.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
                ?? FirstMatch(n => n.Equals("Code", StringComparison.OrdinalIgnoreCase)
                                || n.EndsWith("Code", StringComparison.OrdinalIgnoreCase))
                ?? FirstMatch(n => !string.Equals(n, "Name", StringComparison.OrdinalIgnoreCase))
                ?? columns[0];
        }

        [HttpGet]
        public async Task<JsonResult> GetRecord(string tableName, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return Json(new { success = false, message = "Record key is required." });

            var result = await _data.GetMasterRecordAsync(tableName, key);
            return Json(new
            {
                success = result.Success,
                message = result.Error,
                keyColumn = result.KeyColumn,
                displayLabel = result.DisplayLabel,
                isEditable = result.IsEditable,
                record = result.Record
            });
        }

        [HttpPost]
        public async Task<JsonResult> UpdateRecord([FromBody] RecordUpdateRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Key))
                return Json(new { success = false, message = "Record key is required." });

            var result = await _data.UpdateMasterRecordAsync(
                request.TableName ?? string.Empty,
                request.Key,
                request.Values ?? new Dictionary<string, object?>());

            return Json(new { success = result.Success, message = result.Error });
        }

        [HttpPost]
        public async Task<JsonResult> DeleteRecord([FromBody] RecordKeyRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Key))
                return Json(new { success = false, message = "Record key is required." });

            var result = await _data.DeleteMasterRecordAsync(
                request.TableName ?? string.Empty,
                request.Key);

            return Json(new { success = result.Success, message = result.Error });
        }
    }
}
