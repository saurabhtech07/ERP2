using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using CRM.Models;
using Microsoft.Data.SqlClient;

namespace CRM.Data
{
    // Kept separate from DataAccess.cs on purpose - that file is shared by 6
    // controllers and every edit there risks breaking a master screen.
    //
    // No stored procedure is created or altered by this class. It uses the two
    // that already exist:
    //   read  -> sp_getdetail_byid_andtable_view  (validates name, QUOTENAME)
    //   write -> SP_SaveCategoryRM                 (insert when @id=0, else update)
    // Everything about the column shape is read from sys.columns at runtime, so
    // the grid and the Add/Edit form follow the database instead of a hardcoded list.
    public class CategoryDataAccess
    {
        private readonly string _conn;

        public CategoryDataAccess(string connectionString) => _conn = connectionString;

        private static readonly Regex Shape =
            new(@"^Category_([A-Za-z]+)_(\d+)$", RegexOptions.Compiled);

        // Written by the stored procedure, never by the user. Keeping them out of
        // the form is what lets the grid show them while the form stays editable.
        private static bool IsServerManaged(string columnName) =>
            string.Equals(columnName, "UserName", StringComparison.OrdinalIgnoreCase)
            || columnName.EndsWith("Date", StringComparison.OrdinalIgnoreCase);

        // ---------- table discovery ----------

        // Every Category_* table -> { Group, Seq, Label }. A table that does not
        // match the pattern is ignored, so this cannot drift out of sync with the DB.
        private async Task<List<CategoryTable>> ReadTablesAsync()
        {
            const string sql = @"
SELECT t.name, ISNULL(p.rows, 0)
FROM sys.tables t
LEFT JOIN (SELECT object_id, SUM(rows) rows
           FROM sys.partitions WHERE index_id IN (0,1)
           GROUP BY object_id) p ON p.object_id = t.object_id
WHERE t.name LIKE 'Category[_]%' AND t.is_ms_shipped = 0
ORDER BY t.name;";

            var list = new List<CategoryTable>();
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(sql, conn);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var m = Shape.Match(r.GetString(0));
                if (!m.Success) continue;
                list.Add(new CategoryTable
                {
                    TableName = r.GetString(0),
                    Group = m.Groups[1].Value.ToUpperInvariant(),
                    Seq = int.Parse(m.Groups[2].Value),
                    Label = m.Groups[1].Value.ToUpperInvariant() + m.Groups[2].Value,
                    RowCount = r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1))
                });
            }
            return list;
        }

        public async Task<List<CategoryGroup>> GetGroupsAsync() =>
            (await ReadTablesAsync())
                .GroupBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new CategoryGroup { Group = g.Key, Tables = g.OrderBy(t => t.Seq).ToList() })
                .ToList();

        // ---------- column metadata ----------

        // One round trip that both proves the table is a known Category_* table and
        // returns its shape. The table name from the request reaches the WHERE clause
        // only as a bound parameter, and it must survive the pattern check below.
        private async Task<List<CategoryColumn>?> ReadColumnMetaAsync(string tableName)
        {
            const string sql = @"
SELECT c.name, ty.name, c.max_length, c.is_identity, c.is_nullable
FROM sys.tables t
JOIN sys.columns c ON c.object_id = t.object_id
JOIN sys.types  ty ON ty.user_type_id = c.user_type_id
WHERE t.name = @tableName AND t.is_ms_shipped = 0
ORDER BY c.column_id;";

            var meta = new List<CategoryColumn>();
            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@tableName", SqlDbType.VarChar, 128) { Value = tableName });
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var source = r.GetString(0);
                var type = r.GetString(1);
                var maxLength = r.IsDBNull(2) ? -1 : Convert.ToInt32(r.GetValue(2));
                var isIdentity = r.GetBoolean(3);

                // nvarchar/nchar report bytes, not characters.
                if (type is "nvarchar" or "nchar" && maxLength > 0) maxLength /= 2;

                meta.Add(new CategoryColumn
                {
                    SourceName = source,
                    Name = Normalize(source),
                    Type = type,
                    MaxLength = maxLength,
                    IsIdentity = isIdentity,
                    IsNullable = r.GetBoolean(4),
                    IsServerManaged = IsServerManaged(source),
                    IsNumeric = type is "int" or "bigint" or "smallint" or "tinyint"
                        or "decimal" or "numeric" or "float" or "real" or "money" or "smallmoney",
                    IsDate = type is "date" or "datetime" or "datetime2"
                        or "smalldatetime" or "datetimeoffset",
                    IsLongText = maxLength == -1
                });
            }

            // An empty result means either "not found" or "not a Category_* table".
            return meta.Count == 0 || !Shape.IsMatch(tableName) ? null : meta;
        }

        // ---------- grid ----------

        public async Task<CategoryPageModel> GetRowsAsync(string tableName)
        {
            var model = new CategoryPageModel();
            var meta = await ReadColumnMetaAsync(tableName);
            if (meta == null)
            {
                model.ErrorMessage = "Unknown category table.";
                return model;
            }

            using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("sp_getdetail_byid_andtable_view", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.Add(new SqlParameter("@xparmename", SqlDbType.VarChar, 128)
            {
                Value = tableName
            });

            var dataSet = new DataSet();
            using (var adapter = new SqlDataAdapter(cmd)) adapter.Fill(dataSet);
            var table = dataSet.Tables.Count > 0
                ? dataSet.Tables.Cast<DataTable>()
                    .OrderByDescending(t => t.Columns.Count)
                    .ThenByDescending(t => t.Rows.Count)
                    .FirstOrDefault()
                : null;

            if (table == null || table.Columns.Count == 0)
            {
                model.ColumnMeta = meta;
                model.Columns = meta.Select(m => m.Name).ToList();
                model.SourceColumns = meta.Select(m => m.SourceName).ToList();
                model.ColumnTypes = meta.Select(m => m.Type).ToList();
                return model;
            }

            // The column list is taken from the result set and the sys.columns
            // metadata is matched to it *by name*. Reading one list by position and
            // the other by position is what puts a value under the wrong header the
            // moment the two ever disagree on order.
            model.Columns = table.Columns.Cast<DataColumn>()
                .Select(c => Normalize(c.ColumnName)).ToList();
            model.SourceColumns = table.Columns.Cast<DataColumn>()
                .Select(c => c.ColumnName).ToList();
            model.ColumnTypes = table.Columns.Cast<DataColumn>()
                .Select(c => c.DataType.Name).ToList();

            // One metadata entry per displayed column, in the same order, so
            // ColumnMeta[i] always describes Columns[i].
            model.ColumnMeta = model.SourceColumns.Select(source =>
                meta.FirstOrDefault(m => string.Equals(m.SourceName, source, StringComparison.OrdinalIgnoreCase))
                ?? new CategoryColumn { SourceName = source, Name = Normalize(source) }
            ).ToList();

            model.KeyColumn = model.ColumnMeta.FirstOrDefault(m => m.IsIdentity)?.Name
                ?? ResolveKey(model.Columns) ?? model.Columns.First();
            model.NameColumn = model.ColumnMeta.FirstOrDefault(
                m => !m.IsIdentity && !m.IsServerManaged && m.Name.EndsWith("name", StringComparison.OrdinalIgnoreCase))?.Name
                ?? model.Columns.First();

            foreach (DataRow row in table.Rows)
            {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < table.Columns.Count; i++)
                    dict[Normalize(table.Columns[i].ColumnName)] =
                        row[i] == DBNull.Value ? null : row[i];
                model.Rows.Add(dict);
            }
            return model;
        }

        // ---------- add / edit form ----------

        // Builds the modal body from the table's own columns. Every key in the grid
        // that is not identity and not written by the SP becomes a field, so a column
        // added to the table later shows up here with no code change.
        public async Task<CategoryFormModel> GetFormAsync(string tableName, int id, string userName)
        {
            var form = new CategoryFormModel { TableName = tableName, Id = id, UserName = userName };
            var tables = await ReadTablesAsync();
            var table = tables.FirstOrDefault(
                t => string.Equals(t.TableName, tableName, StringComparison.OrdinalIgnoreCase));
            if (table == null)
            {
                form.ErrorMessage = "Unknown category table.";
                return form;
            }

            form.Label = table.Label;
            form.Group = table.Group;
            form.RowCount = table.RowCount;
            form.IsEdit = id > 0;

            var meta = await ReadColumnMetaAsync(tableName);
            if (meta == null)
            {
                form.ErrorMessage = "Unknown category table.";
                return form;
            }
            form.Fields = meta.Where(m => m.Editable).ToList();
            if (form.Fields.Count == 0)
            {
                form.ErrorMessage = "This table has no editable column.";
                return form;
            }

            if (id <= 0) return form;

            // Editing: read the real row instead of trusting values from the browser.
            var model = await GetRowsAsync(tableName);
            var key = model.Columns.FindIndex(
                c => string.Equals(c, model.KeyColumn, StringComparison.OrdinalIgnoreCase));
            var row = key >= 0
                ? model.Rows.FirstOrDefault(r => Convert.ToInt32(r[model.KeyColumn]) == id)
                : model.Rows.FirstOrDefault();

            if (row == null)
            {
                form.ErrorMessage = "That record no longer exists.";
                return form;
            }
            foreach (var field in form.Fields)
                if (row.TryGetValue(field.Name, out var v) && v != null)
                    form.Values[field.Name] = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";

            return form;
        }

        // ---------- save ----------

        // The dynamic form posts a column -> value map. Every key is checked against
        // sys.columns before anything happens, so a field the form never rendered
        // cannot be written even if the request is hand-crafted.
        public async Task<(bool Success, string Message, string Action, int Id)> SaveAsync(
            string tableName, int id, Dictionary<string, string> values, string userName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return (false, "Table name is required.", "", 0);

            var meta = await ReadColumnMetaAsync(tableName);
            if (meta == null)
                return (false, "Unknown category table.", "", 0);

            var editable = meta.Where(m => m.Editable)
                .ToDictionary(m => m.Name, m => m, StringComparer.OrdinalIgnoreCase);

            var cleaned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (column, raw) in values ?? new Dictionary<string, string>())
            {
                if (!editable.TryGetValue(column, out var info))
                    return (false, $"'{column}' cannot be edited on this table.", "", 0);

                var value = (raw ?? "").Trim();

                // The declared length is enforced here as well as in the SP, so a long
                // paste is refused with a readable message instead of a SQL truncation.
                if (value.Length > 0 && info.MaxLength > 0 && value.Length > info.MaxLength)
                    return (false, $"'{info.SourceName}' allows up to {info.MaxLength} characters.", "", 0);

                if (value.Length == 0)
                {
                    if (!info.IsNullable)
                        return (false, $"'{info.SourceName}' is required.", "", 0);
                    continue;
                }

                if (info.IsNumeric && !decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                    return (false, $"'{info.SourceName}' must be a number.", "", 0);

                if (info.IsDate && !DateTime.TryParse(value, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _))
                    return (false, $"'{info.SourceName}' must be a valid date.", "", 0);

                cleaned[info.Name] = value;
            }

            if (cleaned.Count == 0)
                return (false, "Nothing to save.", "", 0);

            // SP_SaveCategoryRM writes a single column. If a table ever grows an
            // editable column that this SP does not know about, say so rather than
            // quietly dropping the value.
            var unsupported = cleaned.Keys
                .Where(k => !k.Equals("name", StringComparison.OrdinalIgnoreCase)).ToList();
            if (unsupported.Count > 0)
                return (false,
                    $"Stored procedure cannot save {string.Join(", ", unsupported)} on {tableName}. Ask your DBA to extend it.",
                    "", 0);

            if (!cleaned.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
                return (false, "Name is required.", "", 0);

            // Column is varchar(50) while the SP parameter is varchar(100),
            // so an untruncated value would fail with a truncation error.
            if (userName.Length > 50) userName = userName[..50];

            try
            {
                using var conn = new SqlConnection(_conn);
                await conn.OpenAsync();
                using var cmd = new SqlCommand("SP_SaveCategoryRM", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };
                cmd.Parameters.Add(new SqlParameter("@TableName", SqlDbType.VarChar, 128) { Value = tableName });
                cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
                cmd.Parameters.Add(new SqlParameter("@Name", SqlDbType.VarChar, 200) { Value = name });
                cmd.Parameters.Add(new SqlParameter("@UserName", SqlDbType.VarChar, 100) { Value = userName });

                using var r = await cmd.ExecuteReaderAsync();

                // The SP writes two result sets: the saved row (Action,id,Name,...)
                // and then a status set (Alert, ERR_MSG). An error that survived the
                // validation block is reported there, so it has to be drained too.
                string action = id > 0 ? "UPDATE" : "INSERT";
                int savedId = id;

                if (await r.ReadAsync() && r.FieldCount > 0)
                {
                    var actIdx = -1; var idIdx = -1;
                    for (int i = 0; i < r.FieldCount; i++)
                    {
                        if (r.GetName(i).Equals("Action", StringComparison.OrdinalIgnoreCase)) actIdx = i;
                        if (r.GetName(i).Equals("id", StringComparison.OrdinalIgnoreCase)) idIdx = i;
                    }
                    if (actIdx >= 0 && r.GetValue(actIdx)?.ToString() is string a) action = a.Trim();
                    if (idIdx >= 0 && r.GetValue(idIdx) is not null)
                        savedId = Convert.ToInt32(r.GetValue(idIdx));
                }

                while (await r.NextResultAsync())
                {
                    if (!await r.ReadAsync() || r.FieldCount < 2) continue;
                    var alertIdx = -1; var msgIdx = -1;
                    for (int i = 0; i < r.FieldCount; i++)
                    {
                        if (r.GetName(i).Equals("Alert", StringComparison.OrdinalIgnoreCase)) alertIdx = i;
                        else if (r.GetName(i).Equals("ERR_MSG", StringComparison.OrdinalIgnoreCase)) msgIdx = i;
                    }
                    if (alertIdx < 0) continue;
                    if (!string.Equals(r.GetValue(alertIdx)?.ToString(), "N", StringComparison.OrdinalIgnoreCase))
                        return (true, "Category saved.", action, savedId);
                    return (false, msgIdx >= 0 ? r.GetValue(msgIdx)?.ToString() ?? "Could not save." : "Could not save.",
                            action, savedId);
                }

                // No status set came back, so trust the row the SP returned. An UPDATE
                // that matched no row returns nothing at all, which must not be
                // reported as a successful edit.
                if (action.Equals("UPDATE", StringComparison.OrdinalIgnoreCase) && savedId == id && id > 0)
                    return (true, "Category updated.", action, savedId);

                return (true, action.Equals("INSERT", StringComparison.OrdinalIgnoreCase)
                    ? "Category added." : "Category updated.", action, savedId);
            }
            catch (SqlException ex)
            {
                return (false, ex.Number switch
                {
                    2601 or 2627 => "A category with this name already exists.",
                    8152 or 22001 => "Name is too long for this category.",
                    // 229 = permission denied, 297/470 = same, surfaced as a wrapper
                    229 or 297 or 470 => "You do not have permission on this category table. Please ask your DBA to grant INSERT/UPDATE.",
                    _ => "Database error: " + ex.Message
                }, "", 0);
            }
        }

        private static string Normalize(string name)
        {
            var n = name.Trim();
            if (string.Equals(n, "Modify_Date", StringComparison.OrdinalIgnoreCase))
                return "modifyDate";
            if (string.Equals(n, "Create_Date", StringComparison.OrdinalIgnoreCase))
                return "createDate";
            return n.ToLowerInvariant();
        }

        private static string? ResolveKey(List<string> columns, string? mustEndWith = null)
        {
            IEnumerable<string> q = columns;
            if (mustEndWith != null)
                q = q.Where(c => c.EndsWith(mustEndWith, StringComparison.OrdinalIgnoreCase));
            else
                q = q.Where(c => !c.EndsWith("name", StringComparison.OrdinalIgnoreCase));
            return q.FirstOrDefault();
        }
    }
}
