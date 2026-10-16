using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class EconomyController(BaseApiArgument argument, EconomyService service) : AuthorizeApiController(argument)
    {
        [HttpPost]
        public Task<IActionResult> PurchaseSkill(SkillPurchaseRequest request) =>
            TransactionResultAsync<GoldOperationResult>(() => service.PurchaseSkill(request), ApiMessageKeys.EconomySkillFailed);
    }
}
