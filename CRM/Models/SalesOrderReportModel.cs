using System.Collections.Generic;

namespace CRM.Models
{
    public class SalesOrderReportPageViewModel
    {
        public int TotalRecords { get; set; }

        // 100% Dynamic Columns and Rows from Stored Procedure
        public List<string> ColumnNames { get; set; } = new List<string>();
        public List<Dictionary<string, object?>> Rows { get; set; } = new List<Dictionary<string, object?>>();

        public string? ErrorMessage { get; set; }
    }
}