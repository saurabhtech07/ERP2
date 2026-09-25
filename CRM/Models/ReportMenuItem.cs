using System.Collections.Generic;

namespace CRM.Models
{
    public class ReportMenuItem
    {
        public string Tbl_View_Name { get; set; } = string.Empty;
        public string Report_Name { get; set; } = string.Empty;
        public string Report_Type { get; set; } = string.Empty;
        public List<ReportFilterField> FilterFields { get; set; } = new List<ReportFilterField>();
    }

    public class ReportFilterField
    {
        public string FieldName { get; set; } = string.Empty;
        public bool Visible { get; set; } = true;
    }
}
