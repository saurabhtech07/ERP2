using System.Collections.Generic;

namespace CRM.Models
{
    public class JobOrderPageResult
    {
        public List<string> ColumnNames { get; set; } = new List<string>();
        public List<Dictionary<string, object?>> Rows { get; set; } = new List<Dictionary<string, object?>>();

        public int Total { get; set; }
        public int TotalPages { get; set; }

        // Dashboard cards (computed from the filtered dataset)
        public int TotalJobs { get; set; }          // COUNT(DISTINCT Docno)
        public decimal JobQty { get; set; }         // SUM(JobQty)
        public decimal CutQty { get; set; }         // SUM(CutQty)
        public decimal BalCut { get; set; }         // SUM(BalCut)

        public string? ErrorMessage { get; set; }
    }

    public class JobOrderFilterOptions
    {
        public List<string> Items { get; set; } = new List<string>();       // Name field
        public List<string> Categories { get; set; } = new List<string>();  // Cat1 field
        public List<string> Sizes { get; set; } = new List<string>();       // SizeName field
    }
}