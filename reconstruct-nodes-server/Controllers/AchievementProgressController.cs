using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class AchievementProgressController(BaseApiArgument argument, AchievementProgressService service) : AuthorizeApiController(argument)
    {
        /// <summary>
        /// 讀取目前玩家的成就進度；尚未儲存過時回傳 404 封包（前端據此判定為 MISSING）
        /// </summary>
        [HttpPost]
        public virtual async Task<IActionResult> GetProgress()
        {
            AchievementProgressModel? progress = await service.GetProgress();
            if (progress == null) { return StatusCode(404, BackendApiResult<string>.Create(404, "尚無成就進度")); }
            return Ok(BackendApiResult<AchievementProgressModel>.Create(200, string.Empty, progress));
        }

        /// <summary>
        /// 以整份成就快照覆寫目前玩家的資料
        /// </summary>
        [HttpPost]
        public virtual async Task<IActionResult> SaveProgress(AchievementProgressModel model)
        {
            (bool success, string? message) = await TransactionFunc(async () =>
            {
                await service.SaveProgress(model);
            });

            if (!success) { return StatusCode(400, BackendApiResult<string>.Create(400, message ?? "成就進度儲存失敗")); }
            return Ok(BackendApiResult<string>.Create(200, string.Empty, string.Empty));
        }

        /// <summary>
        /// 領取已達成成就的獎勵；標記與發放獎勵在同一交易內完成
        /// </summary>
        [HttpPost]
        public virtual async Task<IActionResult> ClaimReward(AchievementClaimRequest request)
        {
            (bool success, string? message) = await TransactionFunc(async () =>
            {
                await service.ClaimReward(request.AchievementItemId);
            });

            if (!success) { return StatusCode(400, BackendApiResult<string>.Create(400, message ?? "成就獎勵領取失敗")); }
            return Ok(BackendApiResult<string>.Create(200, string.Empty, string.Empty));
        }
    }
}
