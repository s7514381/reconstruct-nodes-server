using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class AdRewardController(BaseApiArgument argument, AdRewardCommand adReward) : AuthorizeApiController(argument)
    {
        /// <summary>今日看廣告進度：已領次數、上限、每次數量，以及可補領的已驗證請求。</summary>
        [HttpPost]
        public virtual Task<IActionResult> Status() =>
            TransactionResultAsync(() => adReward.GetStatus(), ApiMessageKeys.AdRewardFailed,
                commandOwnsTransaction: true);

        /// <summary>領取一次廣告獎勵：需要已通過 SSV 驗證的請求；同請求只會發放一次，重送回傳原回執。</summary>
        [HttpPost]
        public virtual Task<IActionResult> Claim(AdRewardClaimRequest request) =>
            TransactionResultAsync(() => adReward.Claim(request), ApiMessageKeys.AdRewardFailed,
                commandOwnsTransaction: true);
    }

    /// <summary>
    /// AdMob 伺服器端驗證回呼。由 Google 伺服器以 GET 呼叫，不經玩家 ApiFilter／Token，
    /// 一律以簽章驗證來源（見 <see cref="AdRewardSsvService"/>）。
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class AdRewardSsvController(AdRewardSsvService ssv) : BaseController
    {
        [HttpGet]
        public async Task<IActionResult> Verify(CancellationToken cancellationToken)
        {
            var outcome = await ssv.Handle(Request.QueryString.Value ?? "", Request.Query, cancellationToken);
            return outcome switch
            {
                AdRewardSsvService.Outcome.Accepted => Ok(),
                AdRewardSsvService.Outcome.Rejected => BadRequest(),
                _ => StatusCode(503),
            };
        }
    }
}
