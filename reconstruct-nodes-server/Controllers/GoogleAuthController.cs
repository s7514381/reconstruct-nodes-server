using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Auth;
using ReconstructNodesServer.Library.Auth;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Controllers
{
    /// <summary>Google 登入：桌面（PKCE 授權碼 + loopback）與 Android（Credential Manager ID Token）兩條入口。</summary>
    public class GoogleAuthController(
        BaseApiArgument argument,
        GoogleLoginService googleLoginService
        ) : BaseApiController(argument)
    {
        [HttpPost]
        public async Task<IActionResult> Login(GoogleLoginRequest request)
        {
            try
            {
                GoogleSignInResult result = await googleLoginService.LoginAsync(
                    new GoogleLoginInput(request.Code, request.CodeVerifier, request.RedirectUri, request.Locale),
                    HttpContext.RequestAborted);

                return Ok(BackendApiResult<BackendLoginData>.Create(200, string.Empty, new BackendLoginData(result.Token, result.Name)));
            }
            catch (GoogleAuthException ex)
            {
                _logger.LogWarning("Google 登入被拒絕: {Reason} ({Type})", ex.Message, ex.GetType().Name);
                return MapFailure(ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google 登入發生未預期例外");
                return StatusCode(500, BackendApiResult<BackendLoginData>.Create(500, "登入失敗，請稍後再試"));
            }
        }

        /// <summary>Android 原生登入：請求內容含 Google ID Token（敏感值，由 APIRequest 中介層遮蔽記錄）。</summary>
        [HttpPost]
        public async Task<IActionResult> LoginIdToken(GoogleIdTokenLoginRequest request)
        {
            try
            {
                GoogleSignInResult result = await googleLoginService.LoginWithIdTokenAsync(
                    new GoogleIdTokenLoginInput(request.IdToken, request.Locale),
                    HttpContext.RequestAborted);

                return Ok(BackendApiResult<BackendLoginData>.Create(200, string.Empty, new BackendLoginData(result.Token, result.Name)));
            }
            catch (GoogleAuthException ex)
            {
                _logger.LogWarning("Google 原生登入被拒絕: {Reason} ({Type})", ex.Message, ex.GetType().Name);
                return MapFailure(ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google 原生登入發生未預期例外");
                return StatusCode(500, BackendApiResult<BackendLoginData>.Create(500, "登入失敗，請稍後再試"));
            }
        }

        private IActionResult MapFailure(GoogleAuthException ex)
        {
            (int status, string message) = ex switch
            {
                GoogleAuthNotConfiguredException => (503, "Google 登入尚未設定，請使用訪客登入"),
                GoogleLoginInputException => (400, ex.Message),
                GoogleAuthorizationRejectedException => (401, ex.Message),
                GoogleIdTokenInvalidException => (401, "Google 身分驗證失敗，請重新登入"),
                GoogleUpstreamException => (502, "無法連線 Google 驗證服務，請稍後再試"),
                _ => (500, "登入失敗，請稍後再試"),
            };
            return StatusCode(status, BackendApiResult<BackendLoginData>.Create(status, message));
        }
    }

    public class GoogleLoginRequest
    {
        public string? Code { get; set; }
        public string? CodeVerifier { get; set; }
        public string? RedirectUri { get; set; }
        public string? Locale { get; set; }
    }

    public class GoogleIdTokenLoginRequest
    {
        public string? IdToken { get; set; }
        public string? Locale { get; set; }
    }
}
