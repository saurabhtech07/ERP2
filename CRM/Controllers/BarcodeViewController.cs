using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CRM.Data;

namespace CRM.Controllers
{
    [Authorize]
    public class BarcodeViewController : Controller
    {
        private const string TableName = "BARCODE_VIEW";

        private readonly DataAccess _data;

        public BarcodeViewController(IConfiguration configuration)
        {
            _data = new DataAccess(configuration.GetConnectionString("DefaultConnection")!);
        }

        private static Dictionary<string, string> BuildTypeMap(IReadOnlyList<string> columns, IReadOnlyList<string> types)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < columns.Count; i++)
            {
                var type = i < types.Count ? DataAccess.ClassifyType(types[i]) : "text";
                map[columns[i]] = type;
            }
            return map;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Title = "Barcode View";
            ViewBag.pagetitle = "Reports";
            ViewBag.ptitle = "Barcode View Report";

            var data = await _data.GetMasterDataAsync(TableName);

            ViewBag.ColumnNames = data.ColumnNames;
            ViewBag.ColumnTypes = BuildTypeMap(data.ColumnNames, data.ColumnTypes);
            ViewBag.ItemsData = data.Rows;
            ViewBag.ErrorMessage = data.ErrorMessage;
            return View();
        }

        [HttpGet]
        public async Task<JsonResult> GetData()
        {
            var data = await _data.GetMasterDataAsync(TableName);
            var typeMap = BuildTypeMap(data.ColumnNames, data.ColumnTypes);
            return Json(new
            {
                success = data.ErrorMessage == null,
                message = data.ErrorMessage,
                columns = data.ColumnNames,
                types = typeMap,
                rows = data.Rows
            });
        }
    }
}