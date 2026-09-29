using System.Data;
using System.Globalization;
using System.Text;
using CRM.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CRM.Controllers
{
    /// <summary>
    /// Barcode scanning screen for wa.bestit.in.
    ///
    /// One procedure is used, and only one: SP_InsertWebBarcode. It validates
    /// the barcode against Barcode_tbl, writes the scan into web_insert_tbl and
    /// hands back the joined product row. Reading, clearing, exporting and
    /// adding are plain SQL against the same two tables - no other procedure
    /// is created or called.
    /// </summary>
    [Authorize]
    public class BarcodeScanController : Controller
    {
        private const string ScanSp = "SP_InsertWebBarcode";
        private const string ScanTable = "web_insert_tbl";
        private const string ProductView = "Barcode_View";

        /// <summary>The scanning operator this screen runs as.</summary>
        private const string SpUserId = "1";
        private const string UserDisplay = "Saurabh (1)";

        /// <summary>
        /// The same UserId -> name map the procedure uses, so the Username
        /// column matches what the SP would have returned.
        /// </summary>
        private const string UserNameSql = @"
            CASE @pUser
                WHEN 1 THEN 'Saurabh'
                WHEN 2 THEN 'Nilu'
                WHEN 3 THEN 'Faiz'
                ELSE CAST(@pUser AS NVARCHAR(100))
            END";

        private readonly string _connectionString;

        public BarcodeScanController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }

        private static int UserIdNumber => int.Parse(SpUserId, CultureInfo.InvariantCulture);

        // ================================================================ page

        public IActionResult Index()
        {
            ViewBag.Title = "Barcode Scan";
            ViewBag.title = "Barcode Scan";
            ViewBag.ptitle = "Barcode Scan";
            ViewBag.UserDisplay = UserDisplay;
            return View();
        }

        // ================================================================== scan

        /// <summary>
        /// The only procedure on this page. Everything the card needs - the
        /// alert, the message and the product row - comes back in one result
        /// set, so the screen never has to guess.
        /// </summary>
        [HttpPost]
        public async Task<JsonResult> ScanBarcode([FromBody] BarcodeScanRequest? request)
        {
            try
            {
                var barcode = (request?.Barcode ?? string.Empty).Trim();
                if (barcode.Length == 0)
                    return Json(Fail("Barcode is required."));

                if (!int.TryParse(barcode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                    return Json(Fail("Barcode must be numeric."));

                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.CommandType = CommandType.StoredProcedure;
                command.CommandText = ScanSp;
                command.CommandTimeout = 120;
                command.Parameters.AddWithValue("@Barcode", barcode);
                command.Parameters.AddWithValue("@UserId", SpUserId);

                await using var reader = await command.ExecuteReaderAsync();
                var rows = await ReadRowsAsync(reader);
                if (rows.Count == 0)
                    return Json(Fail("No response from " + ScanSp + "."));

                // The procedure returns the user's whole day, not just the row
                // it just wrote, so the scanned row is picked out of the set.
                var row = rows.LastOrDefault(r => ReadText(r, "barcode") == number.ToString(CultureInfo.InvariantCulture))
                          ?? rows[^1];

                var alert = ReadText(row, "Alert");
                var message = ReadText(row, "Message");

                return Json(new
                {
                    success = alert.Equals("Y", StringComparison.OrdinalIgnoreCase),
                    alert = alert,
                    message = message,
                    row = Shape(row),
                    total = rows.Count
                });
            }
            catch (Exception ex)
            {
                return Json(Fail(ex.Message));
            }
        }

        // ================================================================== grid

        /// <summary>
        /// Direct read of the scan table joined to the product view - the same
        /// pair the procedure writes through, so the grid can never disagree
        /// with what the scan card just showed.
        /// </summary>
        [HttpGet]
        public async Task<JsonResult> GetScanned()
        {
            try
            {
                var (columns, rows) = await QueryAsync(GridSql, ("@pUser", UserIdNumber));
                return Json(new { success = true, columns, rows, total = rows.Count });            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message,
                    columns = new List<string>(),
                    rows = new List<Dictionary<string, object?>>()
                });
            }
        }

        /// <summary>
        /// The grid and the export read the same statement, so a file can never
        /// disagree with what is on screen.
        ///
        /// The shape mirrors SP_InsertWebBarcode exactly - Alert, Message,
        /// barcode, Name, Category1, Color, Category3, Username - and the join
        /// and the day filter are the same ones the procedure uses. The
        /// procedure is deliberately NOT called here: it inserts on every call,
        /// so using it as a data source would duplicate a row every refresh.
        /// Nothing is added beyond what the procedure returns.
        /// </summary>
        private const string GridSql = $@"
            SELECT 'Y'                                AS Alert,
                   'Barcode inserted successfully'   AS Message,
                   x.barcode                          AS Barcode,
                   y.Name,
                   y.Category1,
                   y.Color,
                   y.Category3,
                   {UserNameSql}                      AS Username
            FROM [{ScanTable}] x WITH (NOLOCK)
            INNER JOIN [{ProductView}] y WITH (NOLOCK) ON x.barcode = y.barcode
            WHERE x.Userid = @pUser
              AND CAST(x.[date] AS DATE) = CAST(GETDATE() AS DATE)
            ORDER BY x.Id DESC";

        // ================================================================= clear

        [HttpPost]
        public async Task<JsonResult> Clear()
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.CommandText = $"DELETE FROM [{ScanTable}] WHERE Userid = @pUser";
                command.Parameters.AddWithValue("@pUser", UserIdNumber);
                command.CommandTimeout = 60;

                var affected = await command.ExecuteNonQueryAsync();
                return Json(new { success = true, affected, message = $"Cleared {affected} record(s)." });
            }
            catch (Exception ex)
            {
                // The app login has no DELETE grant, so the reason is passed
                // through rather than swallowed behind a generic failure.
                return Json(Fail(ex.Message));
            }
        }

        // ================================================================ export

        [HttpGet]
        public async Task<IActionResult> Export(string format = "csv")
        {
            var (columns, rows) = await QueryAsync(GridSql, ("@pUser", UserIdNumber));

            // When nothing was scanned today the result set is empty, so the
            // header is spelled out from the same statement the grid uses.
            if (columns.Count == 0)
                columns = new List<string>
                {
                    "Alert", "Message", "Barcode",
                    "Name", "Category1", "Color", "Category3", "Username"
                };

            var stamp = DateTime.Now.ToString("ddMMyyyy_HHmm", CultureInfo.InvariantCulture);
            var name = "BarcodeScans_" + stamp;

            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            {
                var csv = BuildCsv(columns, rows);
                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
                return File(bytes, "text/csv; charset=utf-8", name + ".csv");
            }

            return File(BuildSpreadsheetXml(columns, rows), "application/vnd.ms-excel", name + ".xls", true);
        }

        // ============================================================= add master

        /// <summary>
        /// Writes straight into the product tables - no procedure - and stores
        /// the photo under wwwroot/Uploads/Products using the barcode as the
        /// file name, so the browser never controls the path.
        ///
        /// Barcode_View is Barcode_tbl joined to View_Item_Finish, so the
        /// barcode goes into the first and the descriptive fields into the
        /// second. Both are checked against the live catalog before use.
        /// </summary>
        [HttpPost]
        public async Task<JsonResult> SaveMaster(IFormFile? Photo)
        {
            try
            {
                var barcode = (Request.Form["Barcode"].ToString() ?? string.Empty).Trim();
                if (barcode.Length == 0)
                    return Json(Fail("Barcode is required."));

                if (!int.TryParse(barcode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                    return Json(Fail("Barcode must be numeric."));

                var name = Request.Form["Name"].ToString().Trim();
                var category1 = Request.Form["Category1"].ToString().Trim();
                var color = Request.Form["Color"].ToString().Trim();
                var category3 = Request.Form["Category3"].ToString().Trim();

                if (name.Length == 0 && category1.Length == 0 && color.Length == 0 &&
                    category3.Length == 0 && (Photo == null || Photo.Length == 0))
                {
                    return Json(Fail("Nothing to save."));
                }

                var photoPath = string.Empty;
                if (Photo != null && Photo.Length > 0)
                {
                    var saved = await SavePhotoAsync(number, Photo);
                    if (saved == null) return Json(Fail("Photo must be an image under 5 MB."));
                    photoPath = saved;
                }

                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // 1. The barcode itself, if the product view has never seen it.
                var known = await BarcodeExistsAsync(connection, number);
                if (!known)
                    await InsertBarcodeAsync(connection, number);

                // 2. The descriptive fields, which live on View_Item_Finish.
                var finish = await GetItemIdAsync(connection, number);
                if (finish.HasValue)
                    await UpdateItemFinishAsync(connection, finish.Value, name, category1, color, category3);

                var message = photoPath.Length > 0 ? "Saved with photo." : "Saved successfully.";
                if (finish is null) message += " (descriptive fields skipped - no item link found)";

                return Json(new { success = true, message, photoPath });
            }
            catch (Exception ex)
            {
                return Json(Fail(ex.Message));
            }
        }

        private static async Task<bool> BarcodeExistsAsync(SqlConnection connection, int barcode)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(1) FROM [{ProductView}] WITH (NOLOCK) WHERE Barcode = @b";
            command.Parameters.AddWithValue("@b", barcode);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }

        private static async Task InsertBarcodeAsync(SqlConnection connection, int barcode)
        {
            var live = await GetColumnsAsync(connection, "Barcode_tbl");
            var columns = new List<string>();
            var parameters = new List<SqlParameter>();
            var index = 0;

            if (live.Contains("Barcode"))
            {
                columns.Add("[Barcode]");
                parameters.Add(new SqlParameter("@i" + index++, SqlDbType.Int) { Value = barcode });
            }
            if (live.Contains("date"))
            {
                columns.Add("[date]");
                parameters.Add(new SqlParameter("@i" + index++, SqlDbType.DateTime) { Value = DateTime.Now });
            }

            if (columns.Count == 0)
                throw new InvalidOperationException("Barcode_tbl has no Barcode or date column to write.");

            await using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO [Barcode_tbl] ({string.Join(", ", columns)}) " +
                                 $"VALUES ({string.Join(", ", columns.Select((_, i) => "@i" + i))})";
            command.Parameters.AddRange(parameters.ToArray());
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<int?> GetItemIdAsync(SqlConnection connection, int barcode)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT item_id_b FROM [Barcode_tbl] WITH (NOLOCK) WHERE Barcode = @b";
            command.Parameters.AddWithValue("@b", barcode);

            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private static async Task UpdateItemFinishAsync(SqlConnection connection, int itemId,
            string name, string category1, string color, string category3)
        {
            var live = await GetColumnsAsync(connection, "View_Item_Finish");
            var sets = new List<string>();
            var parameters = new List<SqlParameter>();
            var index = 0;

            void AddSet(string column, string value)
            {
                if (!live.Contains(column) || value.Length == 0) return;
                sets.Add($"[{column}] = @s{index}");
                parameters.Add(new SqlParameter("@s" + index++, SqlDbType.VarChar, 200) { Value = value });
            }

            AddSet("Name", name);
            AddSet("Category1", category1);
            // Barcode_View exposes Category2 under the name Color, so that is
            // the column the value belongs in.
            AddSet("Category2", color);
            AddSet("Category3", category3);

            if (sets.Count == 0) return;

            await using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE [View_Item_Finish] SET {string.Join(", ", sets)} WHERE [id] = @id";
            command.Parameters.AddWithValue("@id", itemId);
            command.Parameters.AddRange(parameters.ToArray());
            command.CommandTimeout = 60;
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<HashSet<string>> GetColumnsAsync(SqlConnection connection, string table)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t";
            command.Parameters.AddWithValue("@t", table);

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            return names;
        }

        private async Task<string?> SavePhotoAsync(int barcode, IFormFile photo)
        {
            const long maxBytes = 5 * 1024 * 1024;
            if (photo.Length > maxBytes) return null;

            var extension = Path.GetExtension(photo.FileName)?.ToLowerInvariant();
            if (extension != ".jpg" && extension != ".jpeg" && extension != ".png" && extension != ".webp")
                extension = ".jpg";

            var root = Path.Combine(ResolveContentRoot(), "wwwroot", "Uploads", "Products");
            Directory.CreateDirectory(root);

            var path = Path.Combine(root, barcode + extension);
            await using var stream = System.IO.File.Create(path);
            await photo.CopyToAsync(stream);

            return "/Uploads/Products/" + barcode + extension;
        }

        private string ResolveContentRoot()
        {
            var baseDir = AppContext.BaseDirectory;
            for (var dir = new DirectoryInfo(baseDir); dir != null; dir = dir.Parent)
            {
                if (System.IO.File.Exists(Path.Combine(dir.FullName, "CRM.csproj")) ||
                    Directory.Exists(Path.Combine(dir.FullName, "wwwroot")))
                    return dir.FullName;
            }
            return baseDir;
        }

        // ============================================================== plumbing

        private static object Fail(string message) =>
            new { success = false, alert = "N", message };

        private static string ReadText(Dictionary<string, object?> row, string key) =>
            row.TryGetValue(key, out var value) && value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty
                : string.Empty;

        /// <summary>Values are normalised once so the JSON stays small and the JS stays simple.</summary>
        private static object? Normalize(object? value) => value switch
        {
            null or DBNull => null,
            DateTime date => date.ToString("dd-MM-yyyy HH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeOffset date => date.ToString("dd-MM-yyyy HH:mm:ss", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => value
        };

        private static Dictionary<string, object?> Shape(Dictionary<string, object?> row) =>
            row.ToDictionary(p => p.Key, p => Normalize(p.Value));

        private static async Task<List<Dictionary<string, object?>>> ReadRowsAsync(SqlDataReader reader)
        {
            var columns = new List<string>();
            for (var i = 0; i < reader.FieldCount; i++) columns.Add(reader.GetName(i));

            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < columns.Count; i++)
                {
                    row[columns[i]] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
                }
                rows.Add(row);
            }
            return rows;
        }

        private async Task<(List<string> Columns, List<Dictionary<string, object?>> Rows)> QueryAsync(
            string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 120;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);

            await using var reader = await command.ExecuteReaderAsync();

            var columns = new List<string>();
            for (var i = 0; i < reader.FieldCount; i++) columns.Add(reader.GetName(i));

            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < columns.Count; i++)
                {
                    row[columns[i]] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
                }
                rows.Add(Shape(row));
            }

            return (columns, rows);
        }

        private static string Escape(string value)
        {
            var needsQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            var escaped = value.Replace("\"", "\"\"");
            return needsQuote ? "\"" + escaped + "\"" : escaped;
        }

        private static string BuildCsv(List<string> columns, List<Dictionary<string, object?>> rows)
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", columns.Select(Escape)));
            foreach (var row in rows)
            {
                builder.AppendLine(string.Join(",", columns.Select(c =>
                {
                    var value = row.TryGetValue(c, out var v) && v != null
                        ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty
                        : string.Empty;
                    return Escape(value);
                })));
            }
            return builder.ToString();
        }

        private static byte[] BuildSpreadsheetXml(List<string> columns, List<Dictionary<string, object?>> rows)
        {
            var builder = new StringBuilder();
            builder.Append("<?xml version=\"1.0\"?>\n");
            builder.Append("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" ");
            builder.Append("xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">");
            builder.Append("<Styles><Style ss:ID=\"Header\"><Font ss:Bold=\"1\"/>");
            builder.Append("<Interior ss:Color=\"#F4F4F5\" ss:Pattern=\"Solid\"/></Style></Styles>");
            builder.Append("<Worksheet ss:Name=\"Scans\"><Table>");

            builder.Append("<Row>");
            foreach (var column in columns)
                builder.Append("<Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">")
                       .Append(Xml(column)).Append("</Data></Cell>");
            builder.Append("</Row>");

            foreach (var row in rows)
            {
                builder.Append("<Row>");
                foreach (var column in columns)
                {
                    var text = row.TryGetValue(column, out var value) && value != null
                        ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
                        : string.Empty;
                    var isNumber = double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
                    builder.Append("<Cell><Data ss:Type=\"")
                           .Append(isNumber ? "Number" : "String")
                           .Append("\">").Append(Xml(text)).Append("</Data></Cell>");
                }
                builder.Append("</Row>");
            }

            builder.Append("</Table></Worksheet></Workbook>");
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        private static string Xml(string value) =>
            value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
