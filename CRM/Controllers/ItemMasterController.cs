using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CRM.Data;
using CRM.Models;

namespace CRM.Controllers
{
    [Authorize]
    public class ItemMasterController : Controller
    {
        private readonly DataAccess _data;

        public ItemMasterController(IConfiguration configuration)
        {
            _data = new DataAccess(configuration.GetConnectionString("DefaultConnection")!);
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Title = "Item Master";
            ViewBag.pagetitle = "Master";
            ViewBag.ptitle = "Item Master";

            var data = await _data.GetItemsAsync();

            ViewBag.ColumnNames = data.ColumnNames;
            ViewBag.ItemsData = data.Rows;
            ViewBag.ErrorMessage = data.ErrorMessage;
            return View();
        }

        [HttpGet]
        public async Task<JsonResult> Items()
        {
            var data = await _data.GetItemsAsync();
            return Json(new { success = data.ErrorMessage == null, message = data.ErrorMessage, columns = data.ColumnNames, rows = data.Rows });
        }

        [HttpPost]
        public async Task<JsonResult> Save([FromBody] ItemRaw item)
        {
            try
            {
                await _data.SaveItemAsync(item);
                return Json(new { success = true, message = "Item saved successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpDelete]
        public async Task<JsonResult> Delete(int id)
        {
            try
            {
                await _data.DeleteItemAsync(id);
                return Json(new { success = true, message = "Item deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}