using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CRM.Data;

namespace CRM.Controllers
{
    [Authorize]
    public class StageBalanceController : Controller
    {
        private readonly DataAccess _data;

        public StageBalanceController(IConfiguration configuration)
        {
            _data = new DataAccess(configuration.GetConnectionString("DefaultConnection")!);
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Title = "Stage Balance Report";
            ViewBag.pagetitle = "Reports";
            ViewBag.ptitle = "Stage Balance Report";

            var data = await _data.GetMasterDataAsync("STAGE_BALANCE");

            ViewBag.ColumnNames = data.ColumnNames;
            ViewBag.ItemsData = data.Rows;
            ViewBag.ErrorMessage = data.ErrorMessage;
            return View();
        }

        [HttpGet]
        public async Task<JsonResult> Items()
        {
            var data = await _data.GetMasterDataAsync("STAGE_BALANCE");
            return Json(new { success = data.ErrorMessage == null, message = data.ErrorMessage, columns = data.ColumnNames, rows = data.Rows });
        }
    }
}