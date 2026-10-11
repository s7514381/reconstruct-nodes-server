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
        public virtual async Task<IActionResult> GetTalent()
        {
            TalentModel? talent = await service.GetTalent();
            if (talent == null) { return StatusCode(404, BackendApiResult<string>.Create(404, "尚無天賦樹存檔")); }
            return Ok(BackendApiResult<TalentModel>.Create(200, string.Empty, talent));
        }

        /// <summary>
        /// 以整份天賦樹快照覆寫目前玩家的資料
        /// </summary>
        [HttpPost]
        public virtual async Task<IActionResult> SaveTalent(TalentModel model)
        {
            (bool success, string? message) = await TransactionFunc(async () =>
            {
                await service.SaveTalent(model);
            });

            if (!success) { return StatusCode(400, BackendApiResult<string>.Create(400, message ?? "天賦樹儲存失敗")); }
            return Ok(BackendApiResult<string>.Create(200, string.Empty, string.Empty));
        }
    }
}
