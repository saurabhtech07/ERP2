using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Controllers
{
    [Authorize]
    public class MasterController : Controller
    {
        // Future master table actions can be added here.
        // Client Master has been moved to: ClientMasterController → /ClientMaster/Index
    }
}
