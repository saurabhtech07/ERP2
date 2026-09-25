using System.Data;
using System.Security.Claims;
using CRM.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CRM.Controllers
{
    public class AuthenticationController : Controller
    {
        private readonly IConfiguration _configuration;

        public AuthenticationController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // 1. GET: ReturnUrl को सपोर्ट करने के लिए
        [HttpGet]
        public IActionResult SignInBasic(string? returnUrl = null)
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToLocal(returnUrl);
            }

            var model = new LoginViewModel
            {
                ReturnUrl = returnUrl
            };

            return View(model);
        }

        // 2. POST: Dynamic Login Process
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SignInBasic(LoginViewModel model)
        {
            // Username validation
            if (string.IsNullOrWhiteSpace(model.Username))
            {
                ViewBag.Error = "Please enter your username.";
                return View(model);
            }

            string connectionString = _configuration.GetConnectionString("DefaultConnection")!;

            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    using (SqlCommand cmd = new SqlCommand("Sp_login", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@xuser", model.Username.Trim());
                        cmd.Parameters.AddWithValue("@xpass", model.Password ?? "");

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            DataTable dt = new DataTable();
                            dt.Load(reader);

                            if (dt.Rows.Count > 0)
                            {
                                DataRow row = dt.Rows[0];

                                // Password/User validation
                                if (row.Table.Columns.Contains("Alert") && row["Alert"]?.ToString()?.ToUpper() == "N")
                                {
                                    ViewBag.Error = row.Table.Columns.Contains("ERR_MSG")
                                        ? row["ERR_MSG"]?.ToString()
                                        : "User Name and Password Not Matched";

                                    return View(model);
                                }

                                // Dynamic Claims: डेटाबेस से आने वाले सभी कॉलम्स को सेव करना
                                var claims = new List<Claim>();

                                foreach (DataColumn col in dt.Columns)
                                {
                                    string colValue = row[col]?.ToString() ?? "";
                                    claims.Add(new Claim(col.ColumnName, colValue));
                                }

                                // Standard Name Claim
                                if (dt.Columns.Contains("LOGID"))
                                {
                                    claims.Add(new Claim(ClaimTypes.Name, row["LOGID"]?.ToString() ?? model.Username));
                                }
                                else
                                {
                                    claims.Add(new Claim(ClaimTypes.Name, model.Username));
                                }

                                // Standard NameIdentifier Claim
                                if (dt.Columns.Contains("ID"))
                                {
                                    claims.Add(new Claim(ClaimTypes.NameIdentifier, row["ID"]?.ToString() ?? "0"));
                                }

                                // Identity & Principal
                                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                                var principal = new ClaimsPrincipal(identity);

                                // Remember Me logic
                                var authProperties = new AuthenticationProperties
                                {
                                    IsPersistent = model.RememberMe,
                                    ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : null
                                };

                                // Sign In
                                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

                                // ReturnUrl के अनुसार रीडायरेक्ट
                                return RedirectToLocal(model.ReturnUrl);
                            }
                            else
                            {
                                ViewBag.Error = "Invalid username or password.";
                                return View(model);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Database error: " + ex.Message;
                return View(model);
            }
        }

        // Safe Redirect Method
        private IActionResult RedirectToLocal(string? returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Dashboard");
        }

        // Logout
        public async Task<IActionResult> LogoutBasic()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("SignInBasic", "Authentication");
        }

        // Other Pages
        public IActionResult SignUpBasic() => View();
        public IActionResult PasswordResetBasic() => View();
        public IActionResult PasswordChangeBasic() => View();
        public IActionResult Errors404Basic() => View();
        public IActionResult Errors500() => View();
    }
}
