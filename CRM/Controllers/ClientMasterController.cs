using System.Threading.Tasks;
using CRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Controllers
{
    [Authorize]
    public class ClientMasterController : Controller
    {
        private const string TableName = "Client";

        private readonly IMasterGridService _gridService;

        public ClientMasterController(IMasterGridService gridService)
        {
            _gridService = gridService;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Title = "Client Master";
            ViewBag.pagetitle = "Master";
            ViewBag.ptitle = "Client Master";

            var model = await _gridService.GetPageAsync(TableName, page: 1, pageSize: 10, search: "", sortColumn: "", sortDir: "asc", statusFilter: "all");
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> GetData(int page = 1, int pageSize = 10, string search = "", string sortColumn = "", string sortDir = "asc", string statusFilter = "all")
        {
            var model = await _gridService.GetPageAsync(TableName, page, pageSize, search, sortColumn, sortDir, statusFilter);
            return Json(model);
        }

        public IActionResult Refresh()
        {
            _gridService.InvalidateCache(TableName);
            return RedirectToAction(nameof(Index));
        }
    }
}