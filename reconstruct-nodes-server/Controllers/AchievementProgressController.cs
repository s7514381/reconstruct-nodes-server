using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class AchievementProgressController(BaseApiArgument argument, AchievementProgressCommand service,
        AchievementClaimCommand claim, AchievementResetCommand reset) : AuthorizeApiController(argument)
    {
        /// <summary>
        /// 讀取目前玩家的成就進度；進度與收集由伺服器資料推導
        /// </summary>
        [HttpPost]
        public virtual Task<IActionResult> Get() =>
            TransactionResultAsync<AchievementProgressModel?>(() => service.GetCommand(), ApiMessageKeys.ErrorUnexpected,
                ApiMessageKeys.AchievementMissing, commandOwnsTransaction: true);

        /// <summary>
        /// 領取已達成成就的獎勵：單一請求完成驗證、標記、發獎，並在回執帶回成就快照與領取後的在庫物品。
        /// </summary>
        [HttpPost]
        public virtual Task<IActionResult> ClaimReward(AchievementClaimRequest request) =>
            TransactionResultAsync(() => claim.Execute(request), ApiMessageKeys.AchievementClaimFailed,
                commandOwnsTransaction: true);

        /// <summary>
        /// 測試用：清除成就領取與收集歷史並歸零擊殺／通關進度；僅 Development 環境可用。
        /// </summary>
        [HttpPost]
        public virtual Task<IActionResult> ResetForTesting() =>
            TransactionResultAsync(() => reset.Execute(), ApiMessageKeys.AchievementResetFailed);
    }
}
