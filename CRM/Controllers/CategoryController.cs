using CRM.Data;
using CRM.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Controllers
{
    [Authorize]
    public class CategoryController : Controller
    {
        private readonly CategoryDataAccess _data;

        public CategoryController(IConfiguration configuration) =>
            _data = new CategoryDataAccess(configuration.GetConnectionString("DefaultConnection")!);

        // One sidebar link per group, e.g. /Category?group=RM. The view then lists
        // only the tables belonging to that group in its module dropdown.
        public async Task<IActionResult> Index(string? group = null)
        {
            var groups = await GroupsFor(group);
            if (!groups.Any()) return NotFound();

            ViewBag.pagetitle = "Transaction";
            ViewBag.ptitle = "Categories";
            return View(groups);
        }

        // Same panel as Index, without the layout. The sidebar links are fetched with
        // this when the page is already open, so switching group is not a full reload.
        [HttpGet]
        public async Task<IActionResult> IndexPartial(string group) =>
            PartialView("Partials/_GroupPanel", await GroupsFor(group));

        private async Task<List<CategoryGroup>> GroupsFor(string? group)
        {
            var groups = await _data.GetGroupsAsync();
            if (string.IsNullOrWhiteSpace(group)) return groups;
            return groups.Where(g =>
                string.Equals(g.Group, group, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // The Add/Edit form. It is returned as HTML on purpose - the fields are built
        // from sys.columns on the server, so a column added to the table later shows
        // up as an input without any change here.
        [HttpGet]
        public async Task<IActionResult> ModalForm(string tableName, int? id)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return Content("<div class='alert alert-danger mb-0'>Table name is required.</div>");

            var form = await _data.GetFormAsync(tableName, id ?? 0, CurrentUserName());
            return PartialView("Partials/_CategoryForm", form);
        }

        [HttpGet]
        public async Task<JsonResult> GetRows(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return Json(new { success = false, error = "Table name is required." });

            var model = await _data.GetRowsAsync(tableName);
            if (model.ErrorMessage != null)
                return Json(new { success = false, error = model.ErrorMessage });

            // The payload must be handed to Json() as an object. Wrapping it in
            // JsonSerializer.Serialize() first makes JsonResult write a *string*,
            // which gets JSON-encoded a second time, so the client received
            // "\"{\\\"success\\\":true...}\"" instead of an object and every
            // d.success check was undefined - that surfaced as "Could not load."
            return Json(new
            {
                success = true,
                tableName,
                columns = model.Columns,
                sourceColumns = model.SourceColumns,
                columnTypes = model.ColumnTypes,
                columnMeta = model.ColumnMeta,
                rows = model.Rows,
                keyColumn = model.KeyColumn,
                nameColumn = model.NameColumn
            });
        }

        // No [ValidateAntiForgeryToken] here on purpose: these pages are fetched over
        // XHR and, like DynamicReportController.UpdateRecord, do not carry a token.
        // Adding it made every save fail with 400 before it ever reached the SP.
        [HttpPost]
        public async Task<JsonResult> Save([FromBody] CategorySaveRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.TableName))
                return Json(new { success = false, error = "Table name is required." });

            var (ok, message, action, savedId) = await _data.SaveAsync(
                request.TableName, request.Id, request.Values ?? new(), CurrentUserName());

            return Json(new { success = ok, message, action, id = savedId });
        }

        // Sp_login returns LOGID / ID / EMAIL, so UserName is not a claim here.
        private string CurrentUserName() =>
            User.FindFirst("LOGID")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
            ?? "1";
    }
}
