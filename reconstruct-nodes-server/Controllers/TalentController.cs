using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class TalentController(BaseApiArgument argument, TalentService service, TalentCommand commands) : AuthorizeApiController(argument)
    {
        /// <summary>
        /// 讀取目前玩家的天賦樹；尚未儲存過時回傳 404 封包（前端據此判定為 MISSING）
        /// </summary>
        [HttpPost]
        public virtual Task<IActionResult> GetSnapshot() =>
            TransactionResultAsync<TalentModel?>(() => service.GetSnapshotCommand(), ApiMessageKeys.ErrorUnexpected,
                ApiMessageKeys.TalentMissing, commandOwnsTransaction: true);

        [HttpPost]
        public Task<IActionResult> LearnTalent(TalentNodeCommandRequest request) =>
            TransactionResultAsync(() => commands.Learn(request), ApiMessageKeys.ErrorUnexpected,
                commandOwnsTransaction: true);

        [HttpPost]
        public Task<IActionResult> RefundTalent(TalentNodeCommandRequest request) =>
            TransactionResultAsync(() => commands.RefundNode(request), ApiMessageKeys.ErrorUnexpected,
                commandOwnsTransaction: true);

        [HttpPost]
        public Task<IActionResult> ResetTalents(TalentResetRequest request) =>
            TransactionResultAsync(() => commands.Reset(request), ApiMessageKeys.ErrorUnexpected,
                commandOwnsTransaction: true);
    }
}
