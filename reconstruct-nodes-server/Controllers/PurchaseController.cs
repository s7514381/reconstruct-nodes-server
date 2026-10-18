using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class PurchaseController(BaseApiArgument argument, PurchaseCommand purchase) : AuthorizeApiController(argument)
    {
        /// <summary>
        /// 驗證 Google Play 內購並發放鑽石：同一請求或同一 purchase token 只會發放一次，重送回傳原回執。
        /// </summary>
        [HttpPost]
        public virtual Task<IActionResult> VerifyGooglePlay(GooglePlayPurchaseRequest request) =>
            TransactionResultAsync(() => purchase.Execute(request), ApiMessageKeys.PurchaseFailed,
                commandOwnsTransaction: true);
    }
}
