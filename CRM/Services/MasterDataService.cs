using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CRM.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CRM.Services
{
    public class MasterDataService : IMasterDataService
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        private readonly string _connectionString;
        private readonly ILogger<MasterDataService> _logger;
        private readonly IMemoryCache _cache;

        public MasterDataService(IConfiguration configuration, ILogger<MasterDataService> logger, IMemoryCache cache)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found in appsettings.json.");
            _logger = logger;
            _cache = cache;
        }

        private static string CacheKey(string tableName) => $"Master_{tableName}";

        public void InvalidateCache(string tableName)
        {
            _cache.Remove(CacheKey(tableName));
        }

        public async Task<ClientMasterPageViewModel> GetMasterDataAsync(string tableName)
        {
            if (_cache.TryGetValue(CacheKey(tableName), out ClientMasterPageViewModel cached))
            {
                return cached;
            }

            var viewModel = new ClientMasterPageViewModel();

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    using (var command = new SqlCommand("SP_Get_All_MasterData", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.Add(new SqlParameter("@TableName", SqlDbType.VarChar, 100)
                        {
                            Value = tableName
                        });
                        command.CommandTimeout = 60;

                        await connection.OpenAsync();

                        using (var adapter = new SqlDataAdapter(command))
                        {
                            var dataSet = new DataSet();
                            adapter.Fill(dataSet);

                            DataTable? dataTable = null;
                            if (dataSet.Tables.Count > 0)
                            {
                                // Select table with maximum columns/rows
                                dataTable = dataSet.Tables.Cast<DataTable>()
                                    .OrderByDescending(t => t.Columns.Count)
                                    .ThenByDescending(t => t.Rows.Count)
                                    .FirstOrDefault();
                            }

                            if (dataTable != null && dataTable.Columns.Count > 0)
                            {
                                // Dynamically capture ALL column names in exact order
                                viewModel.ColumnNames = dataTable.Columns.Cast<DataColumn>()
                                    .Select(c => c.ColumnName)
                                    .ToList();

                                // Dynamically store all row values for each column
                                foreach (DataRow row in dataTable.Rows)
                                {
                                    var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                                    for (int i = 0; i < dataTable.Columns.Count; i++)
                                    {
                                        var colName = dataTable.Columns[i].ColumnName;
                                        dict[colName] = NormalizeValue(row[i]);
                                    }
                                    viewModel.Rows.Add(dict);
                                }
                            }
                        }
                    }
                }

                viewModel.TotalRecords = viewModel.Rows.Count;

                if (string.IsNullOrEmpty(viewModel.ErrorMessage))
                {
                    _cache.Set(CacheKey(tableName), viewModel, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = CacheDuration
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while executing SP_Get_All_MasterData for {TableName} in MasterDataService.", tableName);
                viewModel.ErrorMessage = $"Database error: {ex.Message}";
                viewModel.ColumnNames = new List<string>();
                viewModel.Rows = new List<Dictionary<string, object?>>();
                viewModel.TotalRecords = 0;
            }

            return viewModel;
        }

        private static object? NormalizeValue(object value)
        {
            if (value is DBNull) return null;

            if (value is DateTime dt)
            {
                return dt.TimeOfDay == TimeSpan.Zero
                    ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }

            if (value is DateTimeOffset dto)
            {
                return dto.TimeOfDay == TimeSpan.Zero
                    ? dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : dto.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }

            return value;
        }
    }
}
