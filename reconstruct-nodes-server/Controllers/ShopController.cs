using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class ShopController(BaseApiArgument argument, ShopService shop, GoldService gold) : AuthorizeApiController(argument)
    {
        [HttpPost]
        public virtual IActionResult GetCatalog() =>
            Ok(BackendApiResult<ShopService.ShopCatalog>.Create(200, string.Empty, ShopService.Catalog));

        [HttpPost]
        public virtual async Task<IActionResult> GetGold() =>
            Ok(BackendApiResult<long>.Create(200, string.Empty, await gold.GetBalance()));

        [HttpPost]
        public virtual Task<IActionResult> Draw(ShopDrawRequest request) =>
            TransactionResultAsync<ShopDrawResult>(() => shop.Draw(request), ApiMessageKeys.ShopDrawFailed);
    }
}
