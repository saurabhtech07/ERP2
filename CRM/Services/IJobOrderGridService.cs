using System.Threading.Tasks;
using CRM.Models;

namespace CRM.Services
{
    public interface IJobOrderGridService
    {
        Task<JobOrderPageResult> GetPageAsync(
            int page,
            int pageSize,
            string search,
            string sortColumn,
            string sortDir,
            string fromDate = "",
            string toDate = "",
            string item = "",
            string category = "",
            string size = "");

        Task<JobOrderFilterOptions> GetFilterOptionsAsync(string item = "", string category = "", string size = "");

        void InvalidateCache();
    }
}