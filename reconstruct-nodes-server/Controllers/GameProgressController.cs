using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class GameProgressController(BaseApiArgument argument, GameProgressService service) : AuthorizeApiController(argument)
    {
        /// <summary>
        /// 讀取目前玩家的遊戲進度；尚未儲存過時回傳 404 封包（前端據此判定為 MISSING）
        /// </summary>
        [HttpPost]
        public virtual async Task<IActionResult> GetProgress()
        {
            GameProgressModel? progress = await service.GetProgress();
            if (progress == null) { return StatusCode(404, BackendApiResult<string>.Create(404, "尚無遊戲進度")); }
            return Ok(BackendApiResult<GameProgressModel>.Create(200, string.Empty, progress));
        }

        /// <summary>
        /// 以整份遊戲進度快照覆寫目前玩家的資料
        /// </summary>
        [HttpPost]
        public virtual async Task<IActionResult> SaveProgress(GameProgressModel model)
        {
            (bool success, string? message) = await TransactionFunc(async () =>
            {
                await service.SaveProgress(model);
            });

            if (!success) { return StatusCode(400, BackendApiResult<string>.Create(400, message ?? "遊戲進度儲存失敗")); }
            return Ok(BackendApiResult<string>.Create(200, string.Empty, string.Empty));
        }
    }
}
