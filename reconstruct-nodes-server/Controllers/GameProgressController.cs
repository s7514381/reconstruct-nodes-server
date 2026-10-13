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
        public virtual Task<IActionResult> Get() =>
            TransactionResultAsync<GameProgressModel?>(() => service.Get(), ApiMessageKeys.ProgressReadFailed, ApiMessageKeys.ProgressMissing);

        /// <summary>
        /// 以整份遊戲進度快照覆寫目前玩家的資料
        /// </summary>
        [HttpPost]
        public virtual async Task<IActionResult> Save(GameProgressModel model)
        {
            (bool success, ApiMessage? message) = await TransactionFunc(async () =>
            {
                await service.Save(model);
            });

            if (!success) { return StatusCode(400, BackendApiResult<string>.Create(400, message ?? ApiMessageKeys.ProgressSaveFailed)); }
            return Ok(BackendApiResult<string>.Create(200, string.Empty, string.Empty));
        }
    }
}
