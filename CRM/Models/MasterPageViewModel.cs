using System.Collections.Generic;

namespace CRM.Models
{
    public class MasterPageViewModel
    {
        public int TotalRecords { get; set; }
        public List<string> ColumnNames { get; set; } = new List<string>();
        public List<string> ColumnTypes { get; set; } = new List<string>();
        public List<Dictionary<string, object?>> Rows { get; set; } = new List<Dictionary<string, object?>>();
        public string? ErrorMessage { get; set; }
    }
}