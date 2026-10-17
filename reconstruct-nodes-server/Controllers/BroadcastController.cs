using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Broadcast;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Controllers
{
    /// <summary>
    /// 營運用廣播管理（停機公告等）。不經玩家 ApiFilter／Token，改以 X-Admin-Key 驗證；
    /// 遊戲端不呼叫本控制器，只直接讀取 RTDB 公開節點（見 docs/BROADCAST.md）。
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class BroadcastController(
        BroadcastService service,
        FirebaseBroadcastOptions options,
        ILogger<BroadcastController> logger) : BaseController
    {
        public const string AdminKeyHeader = "X-Admin-Key";

        [HttpPost]
        public Task<IActionResult> List(CancellationToken cancellationToken) =>
            Run(async () =>
            {
                var result = await service.ListAsync(cancellationToken);
                return Ok(BackendApiResult<IReadOnlyList<BroadcastItem>>.Create(200, string.Empty, result.Data!));
            });

        [HttpPost]
        public Task<IActionResult> Publish(BroadcastPublishRequest request, CancellationToken cancellationToken) =>
            Run(async () =>
            {
                var result = await service.PublishAsync(request, cancellationToken);
                return result.Outcome switch
                {
                    BroadcastOutcome.Ok => Ok(BackendApiResult<BroadcastItem>.Create(200, string.Empty, result.Data!)),
                    BroadcastOutcome.LimitReached => Fail(409, ApiMessageKeys.BroadcastLimitReached),
                    _ => Invalid(result.InvalidField),
                };
            });

        [HttpPost]
        public Task<IActionResult> Remove(BroadcastRemoveRequest request, CancellationToken cancellationToken) =>
            Run(async () =>
            {
                var result = await service.RemoveAsync(request.Id, cancellationToken);
                return result.Outcome switch
                {
                    BroadcastOutcome.Ok => Ok(BackendApiResult<string>.Create(200, string.Empty, result.Data!)),
                    BroadcastOutcome.NotFound => Fail(404, ApiMessageKeys.BroadcastNotFound),
                    _ => Invalid(result.InvalidField),
                };
            });

        private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
        {
            if (!IsAuthorized()) { return Fail(401, ApiMessageKeys.BroadcastUnauthorized); }
            try
            {
                return await action();
            }
            catch (BroadcastUnavailableException ex)
            {
                logger.LogError(ex, "Broadcast store unavailable");
                return Fail(503, ApiMessageKeys.BroadcastUnavailable);
            }
        }

        /// <summary>未設定 AdminKey 一律拒絕；以固定時間比較避免時序洩漏。</summary>
        private bool IsAuthorized()
        {
            if (string.IsNullOrEmpty(options.AdminKey)) { return false; }
            byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(options.AdminKey));
            byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(Request.Headers[AdminKeyHeader].ToString()));
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }

        private IActionResult Invalid(string? field) =>
            StatusCode(400, BackendApiResult<object>.Create(400,
                new ApiMessage(ApiMessageKeys.BroadcastInvalid, new Dictionary<string, string> { ["field"] = field ?? string.Empty })));

        private IActionResult Fail(int status, string key) =>
            StatusCode(status, BackendApiResult<object>.Create(status, key));
    }
}
