using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CRM.Data;

namespace CRM.Controllers
{
    [Authorize]
    public class JobcardStageBalCutController : Controller
    {
        private readonly DataAccess _data;

        public JobcardStageBalCutController(IConfiguration configuration)
        {
            _data = new DataAccess(configuration.GetConnectionString("DefaultConnection")!);
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Title = "Jobcard Stage Balance Cut";
            ViewBag.pagetitle = "Reports";
            ViewBag.ptitle = "Jobcard Stage Balance Cut";

            var data = await _data.GetMasterDataAsync("JOBCARD_STAGE_BAL_CUT");

            ViewBag.ColumnNames = data.ColumnNames;
            ViewBag.ItemsData = data.Rows;
            ViewBag.ErrorMessage = data.ErrorMessage;
            return View();
        }

        [HttpGet]
        public async Task<JsonResult> Items()
        {
            var data = await _data.GetMasterDataAsync("JOBCARD_STAGE_BAL_CUT");
            return Json(new { success = data.ErrorMessage == null, message = data.ErrorMessage, columns = data.ColumnNames, rows = data.Rows });
        }
    }
}
