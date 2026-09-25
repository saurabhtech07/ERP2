using System.Threading.Tasks;
using CRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Controllers
{
    [Authorize]
    public class SalesOrderReportController : Controller
    {
        private const string TableName = "SALES_ORDER_REPORT";

        private readonly IMasterGridService _gridService;

        public SalesOrderReportController(IMasterGridService gridService)
        {
            _gridService = gridService;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Title = "Sales Order Report";
            ViewBag.pagetitle = "Reports";
            ViewBag.ptitle = "Sales Order Report";

            var model = await _gridService.GetPageAsync(TableName, page: 1, pageSize: 10, search: "", sortColumn: "", sortDir: "asc", statusFilter: "all");
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> GetData(int page = 1, int pageSize = 10, string search = "", string sortColumn = "", string sortDir = "asc", string statusFilter = "all", string fromDate = "", string toDate = "", string client = "", string salesman = "", string salesStatus = "all")
        {
            var model = await _gridService.GetPageAsync(TableName, page, pageSize, search, sortColumn, sortDir, statusFilter, fromDate, toDate, client, salesman, salesStatus);
            return Json(model);
        }

        [HttpGet]
        public async Task<IActionResult> GetFilterOptions(string client = "", string salesman = "", string status = "")
        {
            return Json(await _gridService.GetSalesFilterOptionsAsync(client, salesman, status));
        }

        public IActionResult Refresh()
        {
            _gridService.InvalidateCache(TableName);
            return RedirectToAction(nameof(Index));
        }
    }
}