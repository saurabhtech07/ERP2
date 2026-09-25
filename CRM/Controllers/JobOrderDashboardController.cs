using System.Threading.Tasks;
using CRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Controllers
{
    [Authorize]
    public class JobOrderDashboardController : Controller
    {
        private readonly IJobOrderGridService _gridService;

        public JobOrderDashboardController(IJobOrderGridService gridService)
        {
            _gridService = gridService;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Title = "JOBCARD_STAGE_STATUS";
            ViewBag.pagetitle = "Reports";
            ViewBag.ptitle = "JOBCARD_STAGE_STATUS";

            var model = await _gridService.GetPageAsync(page: 1, pageSize: 10, search: "", sortColumn: "", sortDir: "asc");
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> GetData(int page = 1, int pageSize = 10, string search = "", string sortColumn = "", string sortDir = "asc", string fromDate = "", string toDate = "", string item = "", string category = "", string size = "")
        {
            return Json(await _gridService.GetPageAsync(page, pageSize, search, sortColumn, sortDir, fromDate, toDate, item, category, size));
        }

        [HttpGet]
        public async Task<IActionResult> GetFilterOptions(string item = "", string category = "", string size = "")
        {
            return Json(await _gridService.GetFilterOptionsAsync(item, category, size));
        }

        public IActionResult Refresh()
        {
            _gridService.InvalidateCache();
            return RedirectToAction(nameof(Index));
        }
    }
}