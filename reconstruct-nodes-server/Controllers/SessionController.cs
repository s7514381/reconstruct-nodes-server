using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    /// <summary>
    /// 驗證本機已保存的 Token 是否仍對應有效 session，並回報登入種類（google / guest）。
    /// 不回傳 Token；客戶端據此決定可信恢復或要求重新登入。
    /// </summary>
    public class SessionController(BaseApiArgument argument, PlayerService playerService) : AuthorizeApiController(argument)
    {
        [HttpPost]
        public IActionResult Validate()
        {
            var player = playerService.Player;
            if (player == null)
            {
                return StatusCode(401, BackendApiResult<BackendSessionInfo>.Create(401, ApiMessageKeys.AuthInvalid));
            }

            // IsClientLogin 為權威來源：舊版可能把 GoogleId 寫到訪客列，訪客列不得被回報為 google
            string loginKind = player.IsClientLogin ? "guest"
                : !string.IsNullOrEmpty(player.GoogleId) ? "google"
                : "other";
            return Ok(BackendApiResult<BackendSessionInfo>.Create(200, string.Empty, new BackendSessionInfo(loginKind, player.Name ?? string.Empty)));
        }
    }

    public class BackendSessionInfo
    {
        public BackendSessionInfo(string loginKind, string name)
        {
            LoginKind = loginKind;
            Name = name;
        }

        public string LoginKind { get; }
        public string Name { get; }
    }
}
