using System.Collections.Generic;

namespace CRM.Models
{
    public class CategoryTable
    {
        public string TableName { get; set; } = "";
        public string Group { get; set; } = "";   // RM, SFG, FSG
        public string Label { get; set; } = "";   // RM1, SFG2 ...
        public int Seq { get; set; }
        public int RowCount { get; set; }
    }

    public class CategoryGroup
    {
        public string Group { get; set; } = "";   // RM, SFG, FSG
        public List<CategoryTable> Tables { get; set; } = new List<CategoryTable>();
    }

    // One column of a Category_* table, described from sys.columns. Nothing about
    // the shape is declared in C# - the form, its inputs and its validation are
    // all built from this, so a column added later appears on its own.
    public class CategoryColumn
    {
        public string Name { get; set; } = "";          // normalised (client facing)
        public string SourceName { get; set; } = "";    // real DB column
        public string Type { get; set; } = "";          // varchar, int, datetime ...
        public int MaxLength { get; set; }              // -1 => nvarchar(max)/text
        public bool IsIdentity { get; set; }
        public bool IsNullable { get; set; }
        public bool IsNumeric { get; set; }
        public bool IsDate { get; set; }
        public bool IsLongText { get; set; }

        // Identity, audit and user columns are written by the SP, so they are shown
        // in the grid but never turned into an input.
        public bool IsServerManaged { get; set; }
        public bool Editable => !IsIdentity && !IsServerManaged;
    }

    public class CategoryPageModel
    {
        public List<CategoryGroup> Groups { get; set; } = new List<CategoryGroup>();
        public List<string> Columns { get; set; } = new List<string>();
        public List<string> SourceColumns { get; set; } = new List<string>();
        public List<string> ColumnTypes { get; set; } = new List<string>();
        public List<CategoryColumn> ColumnMeta { get; set; } = new List<CategoryColumn>();
        public List<Dictionary<string, object?>> Rows { get; set; }
            = new List<Dictionary<string, object?>>();
        public string KeyColumn { get; set; } = "id";
        public string NameColumn { get; set; } = "Name";
        public string? ErrorMessage { get; set; }
    }

    // Drives the Add/Edit modal. Every field comes from the table's own metadata.
    public class CategoryFormModel
    {
        public string TableName { get; set; } = "";
        public string Label { get; set; } = "";
        public string Group { get; set; } = "";
        public int RowCount { get; set; }
        public bool IsEdit { get; set; }
        public int Id { get; set; }
        public string UserName { get; set; } = "";
        public List<CategoryColumn> Fields { get; set; } = new List<CategoryColumn>();
        public Dictionary<string, string> Values { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string? ErrorMessage { get; set; }
    }

    public class CategorySaveRequest
    {
        public string TableName { get; set; } = "";
        public int Id { get; set; }
        // column -> value, straight from the dynamic form. The server re-checks
        // every key against sys.columns before it is allowed near any SQL.
        public Dictionary<string, string> Values { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
