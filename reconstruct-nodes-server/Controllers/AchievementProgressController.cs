using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class AchievementProgressController(BaseApiArgument argument, AchievementProgressCommand service) : AuthorizeApiController(argument)
    {
        /// <summary>
        /// 讀取目前玩家的成就進度；進度與收集由伺服器資料推導
        /// </summary>
        [HttpPost]
        public virtual Task<IActionResult> Get() =>
            TransactionResultAsync<AchievementProgressModel?>(() => service.GetCommand(), ApiMessageKeys.ErrorUnexpected,
                ApiMessageKeys.AchievementMissing, commandOwnsTransaction: true);

        /// <summary>
        /// 領取已達成成就的獎勵；標記與發放獎勵在同一交易內完成
        /// </summary>
        [HttpPost]
        public virtual async Task<IActionResult> ClaimReward(AchievementClaimRequest request)
        {
            (bool success, ApiMessage? message) = await TransactionFunc(async () =>
            {
                await service.ClaimReward(request.AchievementItemId);
            });

            if (!success) { return TransactionFailure(message ?? new ApiMessage(ApiMessageKeys.AchievementClaimFailed)); }
            return Ok(BackendApiResult<string>.Create(200, string.Empty, string.Empty));
        }
    }
}
