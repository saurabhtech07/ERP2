using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Controllers
{
    [Authorize]
    public class TransactionController : Controller
    {
        public IActionResult Index()
        {
            ViewBag.Title = "Transaction";
            ViewBag.pagetitle = "Home";
            ViewBag.ptitle = "Transaction";
            return View();
        }
    }
}