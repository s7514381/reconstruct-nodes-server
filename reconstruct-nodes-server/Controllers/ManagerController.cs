using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using ReconstructNodesServer.Filter;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;
using ReconstructNodesServer.Utility;

namespace ReconstructNodesServer.Controllers
{
    /// <summary>
    /// 提供 reconstruct-nodes 前端 BackendApiClient 使用的管理端登入與 session 查詢。
    /// 不套用 ApiFilter：前端不送 Encode header，該檢查為公開演算法，無安全性。
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class ManagerController(PlayerService playerService, IMemoryCache cache) : BaseController
    {
        [HttpPost]
        public async Task<IActionResult> Login(ManagerLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Account) || string.IsNullOrEmpty(request.Password))
            {
                return StatusCode(400, Result<BackendLoginData>(400, "帳號或密碼不可為空"));
            }

            Player? player = await playerService.GetDataByAccount(request.Account);
            if (player == null || !PasswordHasher.Verify(request.Password, player.Password))
            {
                return StatusCode(401, Result<BackendLoginData>(401, "帳號或密碼錯誤"));
            }

            // 每次登入重置 Token，舊 Token 立即失效（與 LoginController 行為一致）
            if (player.Token.HasValue) { cache.Remove(player.Token); }
            player.Token = Guid.NewGuid();
            player.LastLoginDate = DateTime.Now;
            await playerService.UpdateAsync(player);

            return Ok(Result(200, "", new BackendLoginData(player.Token.Value, player.Name)));
        }

        [HttpPost]
        [ServiceFilter(typeof(AuthorizeFilter))]
        public IActionResult GetManagerSession()
        {
            Player? player = playerService.Player;
            if (player?.Token == null)
            {
                return StatusCode(401, Result<BackendLoginData>(401, "身份驗證失敗"));
            }

            return Ok(Result(200, "", new BackendLoginData(player.Token.Value, player.Name)));
        }

        private static BackendApiResult<T> Result<T>(int status, string message, T data = default)
        {
            return BackendApiResult<T>.Create(status, message, data);
        }
    }

    public class ManagerLoginRequest
    {
        public string? Account { get; set; }
        public string? Password { get; set; }
    }
}
