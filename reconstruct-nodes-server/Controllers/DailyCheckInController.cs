using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class DailyCheckInController(BaseApiArgument argument, DailyCheckInCommand checkIn) : AuthorizeApiController(argument)
    {
        /// <summary>讀取本月簽到狀態：伺服器日期、已簽到日期與獎勵表。</summary>
        [HttpPost]
        public virtual Task<IActionResult> Status() =>
            TransactionResultAsync(() => checkIn.GetStatus(), ApiMessageKeys.CheckInFailed,
                commandOwnsTransaction: true);

        /// <summary>簽到今天並發放第 n 格獎勵：同一天或同一請求只會發放一次，重送回傳原回執。</summary>
        [HttpPost]
        public virtual Task<IActionResult> Claim(DailyCheckInClaimRequest request) =>
            TransactionResultAsync(() => checkIn.Claim(request), ApiMessageKeys.CheckInFailed,
                commandOwnsTransaction: true);
    }
}
