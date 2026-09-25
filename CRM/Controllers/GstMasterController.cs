using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CRM.Controllers
{
    [Authorize]
    public class GstMasterController : Controller
    {
        private readonly IConfiguration _configuration;

        public GstMasterController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult Index()
        {
            ViewBag.Title = "GST Master";
            ViewBag.pagetitle = "Master";
            ViewBag.ptitle = "GST Master";
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetGstData()
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
                        cmd.Parameters.AddWithValue("@TableName", "GstMaster");

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            DataTable dt = new DataTable();
                            dt.Load(reader);

                            // Capture all dynamic column names from SP
                            foreach (DataColumn col in dt.Columns)
                            {
                                columns.Add(col.ColumnName);
                            }

                            // Capture all rows preserving exact strings/decimals (e.g. 18.0000)
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
                    message = "Database Error: " + ex.Message
                });
            }
        }
    }
}