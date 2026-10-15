using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class TalentController(BaseApiArgument argument, TalentService service) : AuthorizeApiController(argument)
    {
        /// <summary>
        /// 讀取目前玩家的天賦樹；尚未儲存過時回傳 404 封包（前端據此判定為 MISSING）
        /// </summary>
        [HttpPost]
        public virtual Task<IActionResult> GetSnapshot() =>
            TransactionResultAsync<TalentModel?>(() => service.GetSnapshot(), ApiMessageKeys.ErrorUnexpected, ApiMessageKeys.TalentMissing);

        /// <summary>
        /// 以整份天賦樹快照覆寫目前玩家的資料
        /// </summary>
        [HttpPost]
        public virtual async Task<IActionResult> Save(TalentModel model)
        {
            (bool success, ApiMessage? message) = await TransactionFunc(async () =>
            {
                await service.Save(model);
            });

            if (!success) { return StatusCode(400, BackendApiResult<string>.Create(400, message ?? ApiMessageKeys.TalentSaveFailed)); }
            return SnapshotSaved(model, string.Empty);
        }
    }
}
