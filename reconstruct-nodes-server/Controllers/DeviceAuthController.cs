using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    /// <summary>
    /// 裝置識別的訪客登入。前端以本機 device ID 呼叫即可取得 Token，不需註冊或驗證。
    /// 本控制器只套用 Encode 驗證（繼承 BaseApiController），登入本身不需要 Token。
    /// </summary>
    public class DeviceAuthController(
        BaseApiArgument argument,
        PlayerService playerService,
        IMemoryCache cache
        ) : BaseApiController(argument)
    {
        [HttpPost]
        public async Task<IActionResult> Login(DeviceLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ClientId))
            {
                return StatusCode(400, BackendApiResult<BackendLoginData>.Create(400, ApiMessageKeys.AuthDeviceRequired));
            }

            string clientId = request.ClientId;
            BackendLoginData? data = null;
            try
            {
                (bool isSuccess, ApiMessage? error) = await TransactionFunc(async () =>
                {
                    Player player = await playerService.FindOrCreateGuest(clientId);

                    // 與 LoginController 相同：每次登入重置 Token，舊 Token 立即失效
                    if (player.Token.HasValue) { cache.Remove(player.Token); }
                    player.Token = Guid.NewGuid();
                    player.LastLoginDate = DateTime.Now;
                    player.Locale = request.Locale ?? string.Empty;
                    await playerService.UpdateAsync(player);

                    data = new BackendLoginData(player.Token.Value, player.Name);
                });

                if (!isSuccess)
                {
                    _logger.LogError("裝置登入失敗: {Error}", error);
                    return TransactionFailure(error);
                }
                if (data == null)
                {
                    throw new InvalidOperationException("Device login transaction completed without data");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "裝置登入發生例外");
                return StatusCode(500, BackendApiResult<BackendLoginData>.Create(500, ApiMessageKeys.AuthLoginFailed));
            }

            return Ok(BackendApiResult<BackendLoginData>.Create(200, string.Empty, data));
        }
    }

    public class DeviceLoginRequest
    {
        public string? ClientId { get; set; }
        public string? Locale { get; set; }
    }
}
