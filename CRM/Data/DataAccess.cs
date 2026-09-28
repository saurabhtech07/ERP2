using System.Data;
using Microsoft.Data.SqlClient;
using CRM.Models;

namespace CRM.Data
{
    public class DataAccess
    {
        private readonly string _connString;

        public DataAccess(string connectionString)
        {
            _connString = connectionString;
        }

        public Task<MasterPageViewModel> GetItemsAsync()
        {
            return GetMasterDataAsync("Item_raw");
        }

        public async Task<MasterPageViewModel> GetMasterDataAsync(string tableName)
        {
            var viewModel = new MasterPageViewModel();

            try
            {
                using (var connection = new SqlConnection(_connString))
                {
                    using (var command = new SqlCommand("SP_Get_All_MasterData", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.Add(new SqlParameter("@TableName", SqlDbType.VarChar, 100)
                        {
                            Value = tableName ?? "Item_raw"
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
                                dataTable = dataSet.Tables.Cast<DataTable>()
                                    .OrderByDescending(t => t.Columns.Count)
                                    .ThenByDescending(t => t.Rows.Count)
                                    .FirstOrDefault();
                            }

                            if (dataTable != null && dataTable.Columns.Count > 0)
                            {
                                viewModel.ColumnNames = dataTable.Columns.Cast<DataColumn>()
                                    .Select(c => NormalizeColumnName(c.ColumnName))
                                    .ToList();

                                viewModel.SourceColumnNames = dataTable.Columns.Cast<DataColumn>()
                                    .Select(c => c.ColumnName)
                                    .ToList();

                                viewModel.ColumnTypes = dataTable.Columns.Cast<DataColumn>()
                                    .Select(c => c.DataType.FullName ?? "System.String")
                                    .ToList();

                                foreach (DataRow row in dataTable.Rows)
                                {
                                    var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                                    for (int i = 0; i < dataTable.Columns.Count; i++)
                                    {
                                        var colName = NormalizeColumnName(dataTable.Columns[i].ColumnName);
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
                viewModel.ErrorMessage = ex.Message;
                viewModel.ColumnNames = new List<string>();
                viewModel.SourceColumnNames = new List<string>();
                viewModel.ColumnTypes = new List<string>();
                viewModel.Rows = new List<Dictionary<string, object?>>();
                viewModel.TotalRecords = 0;
            }

            return viewModel;
        }

        private static string NormalizeColumnName(string columnName)
        {
            var n = columnName.Trim();
            if (string.Equals(n, "Modify_Date", StringComparison.OrdinalIgnoreCase))
                return "modifyDate";
            return n.ToLowerInvariant();
        }

        public static string ClassifyType(string? fullName)
        {
            switch (fullName)
            {
                case "System.Int16":
                case "System.Int32":
                case "System.Int64":
                case "System.UInt16":
                case "System.UInt32":
                case "System.UInt64":
                case "System.Byte":
                case "System.SByte":
                case "System.Decimal":
                case "System.Double":
                case "System.Single":
                    return "number";
                case "System.DateTime":
                case "System.DateTimeOffset":
                case "System.DateOnly":
                    return "date";
                default:
                    return "text";
            }
        }

        public async Task SaveItemAsync(ItemRaw item)
        {
            const string sql = "SP_Save_ItemMaster";
            using var conn = new SqlConnection(_connString);
            using var cmd = new SqlCommand(sql, conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@Id", item.Id);
            cmd.Parameters.AddWithValue("@Name", (object?)item.Name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Category1", (object?)item.Category1 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Category2", (object?)item.Category2 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Unit", (object?)item.Unit ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Hsncode", (object?)item.Hsncode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Status", (object?)item.Status ?? DBNull.Value);
            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteItemAsync(int id)
        {
            const string sql = "SP_Delete_ItemMaster";
            using var conn = new SqlConnection(_connString);
            using var cmd = new SqlCommand(sql, conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@Id", id);
            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();
        }

        // ---- Dynamic master record access ---------------------------------
        // Every table/column name used below comes from the static
        // MasterEditability map, never from the request body, so these
        // statements cannot be turned into arbitrary SQL.

        private static string QuoteIdent(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Column name is required.");
            foreach (var ch in name)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '_')
                    throw new InvalidOperationException("Invalid column name.");
            }
            return "[" + name + "]";
        }

        private static string TrimBrackets(string quotedIdent) =>
            quotedIdent.Trim('[', ']');

        private static string BuildKeyPredicate(MasterEditability map, string idParam)
        {
            var sql = QuoteIdent(map.KeyColumn) + " = " + idParam;
            if (!string.IsNullOrWhiteSpace(map.KeyFilter))
                sql += " AND (" + map.KeyFilter + ")";
            return sql;
        }

        public async Task<RecordResult> GetMasterRecordAsync(string viewName, string keyValue)
        {
            var map = MasterEditability.For(viewName);
            if (!map.IsEditable)
                return RecordResult.Fail("This report is read-only.");

            try
            {
                // Restrict to the columns the master actually exposes, rather
                // than SELECT * - these tables carry many more columns
                // (Item_Raw has 24, address has 59) that are not part of
                // the master screen and must not be editable from here.
                var available = await GetWritableColumnsAsync(map.PhysicalTable);
                if (available.Count == 0)
                    return RecordResult.Fail("Table not found: " + map.PhysicalTable);

                var selection = new List<string>();
                foreach (var requested in map.EditableColumns)
                {
                    var actual = available.FirstOrDefault(c =>
                        string.Equals(c, requested, StringComparison.OrdinalIgnoreCase));
                    if (actual != null)
                        selection.Add(QuoteIdent(actual));
                }

                // Always include the key so the caller can identify the record.
                // Compare case-insensitively - the map says "Id" but the table
                // column is "id", and an exact match would insert it twice.
                if (!selection.Any(s =>
                        string.Equals(TrimBrackets(s), map.KeyColumn, StringComparison.OrdinalIgnoreCase)))
                    selection.Insert(0, QuoteIdent(map.KeyColumn));

                var sql = "SELECT " + string.Join(", ", selection) +
                          " FROM " + QuoteIdent(map.PhysicalTable) +
                          " WITH (NOLOCK) WHERE " + BuildKeyPredicate(map, "@KeyValue");

                using var conn = new SqlConnection(_connString);
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@KeyValue", keyValue);
                await conn.OpenAsync();

                using var adapter = new SqlDataAdapter(cmd);
                var dt = new DataTable();
                adapter.Fill(dt);

                if (dt.Rows.Count == 0)
                    return RecordResult.Fail("Record not found.");

                var record = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (DataColumn col in dt.Columns)
                {
                    record[NormalizeColumnName(col.ColumnName)] = dt.Rows[0][col] == DBNull.Value
                        ? null
                        : dt.Rows[0][col];
                }

                var label = record.TryGetValue(map.DisplayColumn.ToLowerInvariant(), out var dv)
                    ? Convert.ToString(dv)
                    : null;

                return RecordResult.Ok(record, map, label ?? keyValue);
            }
            catch (Exception ex)
            {
                return RecordResult.Fail(ex.Message);
            }
        }

        public async Task<RecordResult> UpdateMasterRecordAsync(string viewName, string keyValue, Dictionary<string, object?> values)
        {
            var map = MasterEditability.For(viewName);
            if (!map.IsEditable)
                return RecordResult.Fail("This report is read-only.");

            var allowed = await GetWritableColumnsAsync(map.PhysicalTable);
            var keyColumnNormalized = map.KeyColumn.ToLowerInvariant();

            var sets = new List<string>();
            var parameters = new List<SqlParameter>();
            var index = 0;

            foreach (var pair in values)
            {
                if (string.Equals(pair.Key, keyColumnNormalized, StringComparison.OrdinalIgnoreCase))
                    continue;

                var actual = allowed.FirstOrDefault(c =>
                    string.Equals(NormalizeColumnName(c), NormalizeColumnName(pair.Key), StringComparison.OrdinalIgnoreCase));
                if (actual == null)
                    return RecordResult.Fail("Column '" + pair.Key + "' cannot be updated.");

                index++;
                var paramName = "@p" + index;
                sets.Add(QuoteIdent(actual) + " = " + paramName);
                parameters.Add(new SqlParameter(paramName, SqlDbType.VarChar, 4000)
                {
                    Value = pair.Value ?? (object)DBNull.Value
                });
            }

            if (sets.Count == 0)
                return RecordResult.Fail("No updatable fields were supplied.");

            var sql = "UPDATE " + QuoteIdent(map.PhysicalTable) + " SET " + string.Join(", ", sets) +
                      " WHERE " + BuildKeyPredicate(map, "@KeyValue");

            try
            {
                using var conn = new SqlConnection(_connString);
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@KeyValue", keyValue);
                foreach (var p in parameters)
                    cmd.Parameters.Add(p);
                await conn.OpenAsync();

                var affected = await cmd.ExecuteNonQueryAsync();
                if (affected == 0)
                    return RecordResult.Fail("Record not found, or it was already deleted.");

                return RecordResult.Ok(null, map, keyValue);
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                return RecordResult.Fail("Another record already uses one of these values.");
            }
            catch (SqlException ex) when (ex.Number == 297 || ex.Number == 470)
            {
                return RecordResult.Fail("You do not have permission to update this master. Please ask your DBA to grant UPDATE access.");
            }
            catch (Exception ex)
            {
                return RecordResult.Fail(ex.Message);
            }
        }

        public async Task<RecordResult> DeleteMasterRecordAsync(string viewName, string keyValue)
        {
            var map = MasterEditability.For(viewName);
            if (!map.IsEditable)
                return RecordResult.Fail("This report is read-only.");

            var sql = "DELETE FROM " + QuoteIdent(map.PhysicalTable) +
                      " WHERE " + BuildKeyPredicate(map, "@KeyValue");

            try
            {
                using var conn = new SqlConnection(_connString);
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@KeyValue", keyValue);
                await conn.OpenAsync();

                var affected = await cmd.ExecuteNonQueryAsync();
                if (affected == 0)
                    return RecordResult.Fail("Record not found, or it was already deleted.");

                return RecordResult.Ok(null, map, keyValue);
            }
            catch (SqlException ex) when (ex.Number == 547)
            {
                return RecordResult.Fail("This record is used elsewhere and cannot be deleted.");
            }
            catch (SqlException ex) when (ex.Number == 297 || ex.Number == 470)
            {
                return RecordResult.Fail("You do not have permission to delete records. Please ask your DBA to grant DELETE access.");
            }
            catch (Exception ex)
            {
                return RecordResult.Fail(ex.Message);
            }
        }

        private async Task<List<string>> GetWritableColumnsAsync(string physicalTable)
        {
            using var conn = new SqlConnection(_connString);
            using var cmd = new SqlCommand(
                "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@Table) ORDER BY column_id", conn);
            cmd.Parameters.AddWithValue("@Table", physicalTable);
            await conn.OpenAsync();

            var names = new List<string>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                names.Add(reader.GetString(0));
            return names;
        }

        public async Task<List<ReportMenuItem>> GetWebTblMasterAsync()
        {
            var menuItems = new List<ReportMenuItem>();
            try
            {
                using var conn = new SqlConnection(_connString);
                using var cmd = new SqlCommand("SP_GetWebTblMaster", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                await conn.OpenAsync();
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var viewName = ReadStringValue(reader, "Tbl_View_Name", "TblViewName");
                    var menuItem = new ReportMenuItem
                    {
                        Tbl_View_Name = viewName,
                        Report_Name = ReadStringValue(reader, "Report_Name", "ReportName"),
                        Report_Type = ReadStringValue(reader, "Menu_Head", "MenuHead", "Report_Type", "ReportType", "Type"),
                        FilterFields = ReadFilterFields(reader),
                        CardFields = ReadCardFields(reader)
                    };
                    menuItem.ApplyEditability(MasterEditability.For(viewName));
                    menuItems.Add(menuItem);
                }
            }
            catch (Exception)
            {
                // In production, log the exception here.
                throw;
            }
            return menuItems;
        }

        private static List<ReportFilterField> ReadFilterFields(SqlDataReader reader)
        {
            var filterFields = new List<ReportFilterField>();
            AddPositionalFilterFields(reader, filterFields);
            ApplyVisibility(reader, filterFields);
            return filterFields;
        }

        private static List<ReportCardField> ReadCardFields(SqlDataReader reader)
        {
            var cardFields = new List<ReportCardField>();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                var normalizedColumnName = NormalizeMetadataColumnName(reader.GetName(i));
                if (!normalizedColumnName.StartsWith("kpi", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!int.TryParse(normalizedColumnName.Substring("kpi".Length), out var slot) || slot < 2 || slot > 4)
                {
                    continue;
                }

                if (reader.IsDBNull(i))
                {
                    continue;
                }

                var text = reader.GetValue(i)?.ToString()?.Trim() ?? string.Empty;
                if (text.Length == 0)
                {
                    continue;
                }

                var parts = text.Split(new[] { '|' }, 2);
                var fieldName = parts[0].Trim();
                if (fieldName.Length == 0)
                {
                    continue;
                }

                cardFields.Add(new ReportCardField
                {
                    Slot = slot,
                    FieldName = fieldName,
                    Label = parts.Length > 1 ? parts[1].Trim() : string.Empty
                });
            }

            return cardFields.OrderBy(x => x.Slot).ToList();
        }

        private static void AddPositionalFilterFields(SqlDataReader reader, List<ReportFilterField> filterFields)
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var normalizedColumnName = NormalizeMetadataColumnName(reader.GetName(i));
                if (!normalizedColumnName.StartsWith("filter", StringComparison.Ordinal))
                {
                    continue;
                }

                var positionText = normalizedColumnName.Substring("filter".Length);
                if (!int.TryParse(positionText, out _))
                {
                    continue;
                }

                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                AddFilterValue(filterFields, value, string.Empty);
            }
        }

        private static void ApplyVisibility(SqlDataReader reader, List<ReportFilterField> filterFields)
        {
            foreach (var filterField in filterFields)
            {
                var visibilityColumnNames = new[]
                {
                    filterField.FieldName + "Visible",
                    filterField.FieldName + "_Visible",
                    "Visible" + filterField.FieldName,
                    "Is" + filterField.FieldName + "Visible"
                };

                if (TryReadValue(reader, visibilityColumnNames, out var visibilityValue) && !ParseBooleanValue(visibilityValue, true))
                {
                    filterField.Visible = false;
                }
            }
        }

        private static void AddFilterValue(List<ReportFilterField> filterFields, object? value, string defaultFieldName)
        {
            if (value == null || value == DBNull.Value)
            {
                return;
            }

            if (value is bool booleanValue)
            {
                AddFilterField(filterFields, defaultFieldName, booleanValue);
                return;
            }

            var text = value.ToString()?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                return;
            }

            var values = text.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in values)
            {
                var fieldName = item.Trim();
                if (fieldName.Length == 0)
                {
                    continue;
                }

                if (IsBooleanFalse(fieldName))
                {
                    AddFilterField(filterFields, defaultFieldName, false);
                }
                else if (IsBooleanTrue(fieldName))
                {
                    AddFilterField(filterFields, defaultFieldName, true);
                }
                else
                {
                    AddFilterField(filterFields, fieldName, true);
                }
            }
        }

        private static void AddFilterField(List<ReportFilterField> filterFields, string fieldName, bool visible)
        {
            var normalizedName = fieldName.Trim();
            if (normalizedName.Length == 0 || filterFields.Any(x => string.Equals(x.FieldName, normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            filterFields.Add(new ReportFilterField
            {
                FieldName = normalizedName,
                Visible = visible
            });
        }

        private static bool ParseBooleanValue(object? value, bool defaultValue)
        {
            if (value is bool booleanValue)
            {
                return booleanValue;
            }

            var text = value?.ToString()?.Trim().ToLowerInvariant() ?? string.Empty;
            if (text == "true" || text == "1" || text == "yes" || text == "y" || text == "on")
            {
                return true;
            }

            if (text == "false" || text == "0" || text == "no" || text == "n" || text == "off")
            {
                return false;
            }

            return defaultValue;
        }

        private static bool IsBooleanTrue(string value)
        {
            var normalizedValue = value.Trim().ToLowerInvariant();
            return normalizedValue == "true" || normalizedValue == "1" || normalizedValue == "yes" || normalizedValue == "y" || normalizedValue == "on";
        }

        private static bool IsBooleanFalse(string value)
        {
            var normalizedValue = value.Trim().ToLowerInvariant();
            return normalizedValue == "false" || normalizedValue == "0" || normalizedValue == "no" || normalizedValue == "n" || normalizedValue == "off";
        }

        private static string ReadStringValue(SqlDataReader reader, params string[] columnNames)
        {
            return TryReadValue(reader, columnNames, out var value)
                ? value?.ToString()?.Trim() ?? string.Empty
                : string.Empty;
        }

        private static bool TryReadValue(SqlDataReader reader, IEnumerable<string> columnNames, out object? value)
        {
            var normalizedColumnNames = columnNames
                .Select(NormalizeMetadataColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (normalizedColumnNames.Contains(NormalizeMetadataColumnName(reader.GetName(i))))
                {
                    value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static string NormalizeMetadataColumnName(string value)
        {
            return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }
    }
}