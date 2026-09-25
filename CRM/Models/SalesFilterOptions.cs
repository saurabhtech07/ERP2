using System.Collections.Generic;

namespace CRM.Models
{
    public class SalesFilterOptions
    {
        public List<string> Clients { get; set; } = new List<string>();
        public List<string> Salesmen { get; set; } = new List<string>();
        public List<string> Statuses { get; set; } = new List<string>();
    }
}