using System.Collections.Generic;

namespace CRM.Models
{
    public class MasterDataPageResult
    {
        public List<string> ColumnNames { get; set; } = new List<string>();
        public List<Dictionary<string, object?>> Rows { get; set; } = new List<Dictionary<string, object?>>();

        public int Total { get; set; }
        public int TotalPages { get; set; }

        public int ActiveCount { get; set; }
        public int InactiveCount { get; set; }
        public int GstCount { get; set; }

        public decimal TotalOrders { get; set; }
        public decimal OpenOrders { get; set; }
        public decimal PendingQty { get; set; }
        public decimal InvoicedQty { get; set; }
        public decimal OrderValue { get; set; }

        public string? ErrorMessage { get; set; }
    }
}