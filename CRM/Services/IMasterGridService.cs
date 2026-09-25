using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CRM.Models;

namespace CRM.Services
{
    public interface IMasterGridService
    {
        Task<MasterDataPageResult> GetPageAsync(
            string tableName,
            int page,
            int pageSize,
            string search,
            string sortColumn,
            string sortDir,
            string statusFilter,
            string fromDate = "",
            string toDate = "",
            string client = "",
            string salesman = "",
            string salesStatus = "all");

        Task<SalesFilterOptions> GetSalesFilterOptionsAsync(string client = "", string salesman = "", string status = "");

        void InvalidateCache(string tableName);
    }
}