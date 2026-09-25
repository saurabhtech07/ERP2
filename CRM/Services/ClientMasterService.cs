using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using CRM.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CRM.Services
{
    public class ClientMasterService : IClientMasterService
    {
        private const string CacheKey = "ClientMaster_GetAll";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        private readonly string _connectionString;
        private readonly ILogger<ClientMasterService> _logger;
        private readonly IMemoryCache _cache;

        public ClientMasterService(IConfiguration configuration, ILogger<ClientMasterService> logger, IMemoryCache cache)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found in appsettings.json.");
            _logger = logger;
            _cache = cache;
        }

        public async Task<ClientMasterPageViewModel> GetClientMasterDataAsync()
        {
            if (_cache.TryGetValue(CacheKey, out ClientMasterPageViewModel cached))
            {
                return cached;
            }

            var viewModel = await LoadFromDatabaseAsync();
            if (string.IsNullOrEmpty(viewModel.ErrorMessage))
            {
                _cache.Set(CacheKey, viewModel, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheDuration
                });
            }

            return viewModel;
        }

        public void InvalidateCache()
        {
            _cache.Remove(CacheKey);
        }

        private async Task<ClientMasterPageViewModel> LoadFromDatabaseAsync()
        {
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
                            Value = "Client"
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
                                        dict[colName] = row[i] == DBNull.Value ? null : row[i];
                                    }
                                    viewModel.Rows.Add(dict);
                                }
                            }
                        }
                    }
                }

                viewModel.TotalRecords = viewModel.Rows.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while executing SP_Get_All_MasterData for Client in ClientMasterService.");
                viewModel.ErrorMessage = $"Database error: {ex.Message}";
                viewModel.ColumnNames = new List<string>();
                viewModel.Rows = new List<Dictionary<string, object?>>();
                viewModel.TotalRecords = 0;
            }

            return viewModel;
        }
    }
}