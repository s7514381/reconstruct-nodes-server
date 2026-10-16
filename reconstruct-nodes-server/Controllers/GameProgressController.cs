using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;
using ReconstructNodesServer.Models;

namespace ReconstructNodesServer.Controllers
{
    public class GameProgressController(BaseApiArgument argument, GameProgressCommand service,
        BackgroundSyncCommand background, PlayerLifecycleService lifecycle) : AuthorizeApiController(argument)
    {
        /// <summary>
        /// 讀取目前玩家的遊戲進度；尚未儲存過時回傳 404 封包（前端據此判定為 MISSING）
        /// </summary>
        [HttpPost]
        public virtual Task<IActionResult> Get() =>
            TransactionResultAsync<GameProgressModel?>(() => service.GetCommand(), ApiMessageKeys.ProgressReadFailed,
                ApiMessageKeys.ProgressMissing, commandOwnsTransaction: true);

        [HttpPost]
        [ProducesResponseType(typeof(BackendApiResult<BackgroundSyncResponse>), 200)]
        public virtual Task<IActionResult> BackgroundSync(BackgroundSyncRequest request) =>
            TransactionResultAsync(() => background.Execute(request), ApiMessageKeys.ProgressSaveFailed,
                commandOwnsTransaction: true);

        [HttpPost]
        [ProducesResponseType(typeof(BackendApiResult<PlayerInitializationResponse>), 200)]
        public virtual Task<IActionResult> Initialize() =>
            TransactionResultAsync(() => lifecycle.Initialize(), ApiMessageKeys.ProgressSaveFailed);

        [HttpPost]
        public virtual Task<IActionResult> WaveComplete(WaveCompleteRequest request) =>
            TransactionResultAsync(() => lifecycle.Complete(request), ApiMessageKeys.ProgressSaveFailed);

    }
}
