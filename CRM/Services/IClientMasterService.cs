using System.Threading.Tasks;
using CRM.Models;

namespace CRM.Services
{
    public interface IClientMasterService
    {
        Task<ClientMasterPageViewModel> GetClientMasterDataAsync();
        void InvalidateCache();
    }
}
