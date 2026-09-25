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
                var reportName = menuItems.FirstOrDefault(x => x.Tbl_View_Name == tableName)?.Report_Name ?? string.Empty;
                return Json(new { 
                    success = data.ErrorMessage == null, 
                    message = data.ErrorMessage, 
                    tableName = tableName,
                    reportName = reportName,
                    columns = data.ColumnNames, 
                    rows = data.Rows 
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
