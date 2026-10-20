using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class ChallengeController(BaseApiArgument argument, ChallengeCommand challenge) : AuthorizeApiController(argument)
    {
        /// <summary>讀取各挑戰關卡今日剩餘次數與通關獎勵。</summary>
        [HttpPost]
        public virtual Task<IActionResult> Status() =>
            TransactionResultAsync(() => challenge.GetStatus(), ApiMessageKeys.ChallengeFailed,
                commandOwnsTransaction: true);

        /// <summary>領取目前挑戰關卡的通關獎勵：每次進入只發放一次，重送回傳原回條。</summary>
        [HttpPost]
        public virtual Task<IActionResult> Claim(ChallengeClaimRequest request) =>
            TransactionResultAsync(() => challenge.Claim(request), ApiMessageKeys.ChallengeFailed,
                commandOwnsTransaction: true);
    }
}
