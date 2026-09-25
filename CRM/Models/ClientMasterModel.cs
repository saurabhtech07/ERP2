using System;
using System.Collections.Generic;
using System.Data;

namespace CRM.Models
{
    public class ClientMasterPageViewModel
    {
        public int TotalRecords { get; set; }
        public int LastWeekRecords { get; set; }
        public int ActiveRecords { get; set; }
        public int NonActiveRecords { get; set; }

        // 100% Dynamic Columns and Rows from Stored Procedure
        public List<string> ColumnNames { get; set; } = new List<string>();
        public List<Dictionary<string, object?>> Rows { get; set; } = new List<Dictionary<string, object?>>();

        public string? ErrorMessage { get; set; }
    }
}
