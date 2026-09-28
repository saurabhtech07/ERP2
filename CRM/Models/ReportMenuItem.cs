using System.Collections.Generic;

namespace CRM.Models
{
    public class ReportMenuItem
    {
        public string Tbl_View_Name { get; set; } = string.Empty;
        public string Report_Name { get; set; } = string.Empty;
        public string Report_Type { get; set; } = string.Empty;
        public List<ReportFilterField> FilterFields { get; set; } = new List<ReportFilterField>();
        public List<ReportCardField> CardFields { get; set; } = new List<ReportCardField>();

        public bool IsEditable { get; set; }
        public string? PhysicalTable { get; set; }
        public string? KeyColumn { get; set; }
        public string? KeyFilter { get; set; }
        public string? DisplayColumn { get; set; }

        public bool IsViewOnly => !IsEditable;

        public void ApplyEditability(MasterEditability editability)
        {
            IsEditable = editability.IsEditable;
            PhysicalTable = editability.PhysicalTable;
            KeyColumn = editability.KeyColumn;
            KeyFilter = editability.KeyFilter;
            DisplayColumn = editability.DisplayColumn;
        }
    }

    public class MasterEditability
    {
        public string ViewName { get; set; } = string.Empty;
        public bool IsEditable { get; set; }
        public string PhysicalTable { get; set; } = string.Empty;
        public string KeyColumn { get; set; } = string.Empty;
        public string KeyFilter { get; set; } = string.Empty;
        public string DisplayColumn { get; set; } = "Name";

        // Columns the master screen is allowed to touch. Mirrors what
        // SP_Get_All_MasterData returns for this view, so the edit modal
        // never exposes the dozens of extra columns in the physical table.
        public List<string> EditableColumns { get; set; } = new List<string>();

        private static readonly Dictionary<string, MasterEditability> Map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["ITEM_RAW"] = new MasterEditability
                {
                    ViewName = "ITEM_RAW",
                    IsEditable = true,
                    PhysicalTable = "Item_Raw",
                    KeyColumn = "Id",
                    DisplayColumn = "Name",
                    EditableColumns = new List<string>
                    {
                        "Id", "Name", "Category1", "Category2",
                        "Unit", "Status", "Hsncode", "Modify_Date"
                    }
                },
                ["CLIENT"] = new MasterEditability
                {
                    ViewName = "CLIENT",
                    IsEditable = true,
                    PhysicalTable = "address",
                    KeyColumn = "Id",
                    KeyFilter = "tp = 'D'",
                    DisplayColumn = "Name",
                    EditableColumns = new List<string>
                    {
                        "Id", "Code", "Name", "GstnBill", "Add1", "Add2", "City", "Pin"
                    }
                },
                ["SUPPLIER"] = new MasterEditability
                {
                    ViewName = "SUPPLIER",
                    IsEditable = true,
                    PhysicalTable = "address",
                    KeyColumn = "Id",
                    KeyFilter = "tp = 'C'",
                    DisplayColumn = "Name",
                    EditableColumns = new List<string>
                    {
                        "Id", "Code", "Name", "GstnBill", "Add1", "Add2", "City", "Pin"
                    }
                },
                ["GSTMASTER"] = new MasterEditability
                {
                    ViewName = "GSTMASTER",
                    IsEditable = true,
                    PhysicalTable = "GstMaster",
                    KeyColumn = "Id",
                    DisplayColumn = "Name",
                    EditableColumns = new List<string>
                    {
                        "Id", "HsnCode", "Name", "IGST1_PER", "UserName", "ModifyDate"
                    }
                }
            };

        public static MasterEditability For(string? viewName) =>
            viewName != null && Map.TryGetValue(viewName, out var found)
                ? found
                : new MasterEditability { ViewName = viewName ?? string.Empty, IsEditable = false };
    }

    public class ReportFilterField
    {
        public string FieldName { get; set; } = string.Empty;
        public bool Visible { get; set; } = true;
    }

    public class ReportCardField
    {
        public int Slot { get; set; }
        public string FieldName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
}
