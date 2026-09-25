using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CRM.Controllers
{
    [Authorize]
    public class SupplierMasterController : Controller
    {
        private readonly IConfiguration _configuration;

        public SupplierMasterController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // 1. Page Load View
        [HttpGet]
        public IActionResult Index()
        {
            ViewBag.Title = "Supplier Master";
            ViewBag.pagetitle = "Master";
            ViewBag.ptitle = "Supplier Master";
            return View();
        }

        // 2. AJAX Endpoint: Returns live data and dynamic schema from SP
        [HttpGet]
        public async Task<IActionResult> GetSupplierData()
        {
            string connectionString = _configuration.GetConnectionString("DefaultConnection")!;
            var columns = new List<string>();
            var rows = new List<Dictionary<string, object>>();

            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand("SP_Get_All_MasterData", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@TableName", "Supplier");

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            DataTable dt = new DataTable();
                            dt.Load(reader);

                            // Capture all dynamic column names from the SP result
                            foreach (DataColumn col in dt.Columns)
                            {
                                columns.Add(col.ColumnName);
                            }

                            // Capture all rows as key-value dictionaries
                            foreach (DataRow dr in dt.Rows)
                            {
                                var row = new Dictionary<string, object>();
                                foreach (DataColumn col in dt.Columns)
                                {
                                    if (dr[col] == DBNull.Value)
                                    {
                                        row[col.ColumnName] = "";
                                    }
                                    else if (dr[col] is DateTime dtVal)
                                    {
                                        row[col.ColumnName] = dtVal.ToString("yyyy-MM-dd HH:mm:ss");
                                    }
                                    else if (dr[col] is decimal decVal)
                                    {
                                        row[col.ColumnName] = decVal.ToString("0.0000");
                                    }
                                    else
                                    {
                                        row[col.ColumnName] = dr[col].ToString() ?? "";
                                    }
                                }
                                rows.Add(row);
                            }
                        }
                    }
                }

                return Json(new
                {
                    success = true,
                    columns = columns,
                    data = rows,
                    total = rows.Count
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Database error: " + ex.Message
                });
            }
        }
    }
}
