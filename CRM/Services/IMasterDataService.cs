using System.Threading.Tasks;
using CRM.Models;

namespace CRM.Services
{
    public interface IMasterDataService
    {
        Task<ClientMasterPageViewModel> GetMasterDataAsync(string tableName);
        void InvalidateCache(string tableName);
    }
}
