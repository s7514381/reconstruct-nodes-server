using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class ShopController(BaseApiArgument argument, ShopCommand shop, GoldCommand gold) : AuthorizeApiController(argument)
    {
        [HttpPost]
        public virtual IActionResult GetCatalog() =>
            Ok(BackendApiResult<ShopCommand.ShopCatalog>.Create(200, string.Empty, ShopCommand.Catalog));

        [HttpPost]
        public virtual async Task<IActionResult> GetGold() =>
            Ok(BackendApiResult<long>.Create(200, string.Empty, await gold.GetBalance()));

        [HttpPost]
        public virtual Task<IActionResult> Draw(ShopDrawRequest request) =>
            TransactionResultAsync<ShopDrawResult>(() => shop.Draw(request), ApiMessageKeys.ShopDrawFailed,
                commandOwnsTransaction: true);
    }
}
