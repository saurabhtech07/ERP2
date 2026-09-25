using System.Threading.Tasks;
using CRM.Models;

namespace CRM.Services
{
    public interface ISalesOrderReportService
    {
        Task<SalesOrderReportPageViewModel> GetSalesOrderReportDataAsync();
        void InvalidateCache();
    }
}