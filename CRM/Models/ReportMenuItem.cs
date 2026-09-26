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
