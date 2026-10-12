#nullable enable
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ReconstructNodesServer.Library.Auth
{
    /// <summary>Google 登入請求；Code 與 CodeVerifier 為敏感值，不得記錄。</summary>
    public sealed record GoogleLoginInput(string? Code, string? CodeVerifier, string? RedirectUri, string? Locale);

    /// <summary>Android 原生登入請求；IdToken 為敏感值，不得記錄。</summary>
    public sealed record GoogleIdTokenLoginInput(string? IdToken, string? Locale);

    /// <summary>本遊戲簽發的 Session：Token 僅代表後端 session，不是 Google 憑證。</summary>
    public sealed record GoogleSignInResult(Guid Token, string Name);

    /// <summary>以已驗證的 Google sub 找到或建立 Google 玩家，並輪替本遊戲 Token。實作必須不接觸訪客資料。</summary>
    public interface IGoogleSessionStore
    {
        Task<GoogleSignInResult> SignInAsync(string googleSubject, string displayName, string locale, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Google 登入流程：
    /// 桌面：驗證輸入 → 交換授權碼 → 驗證 ID Token（audience = ClientId）→ 以 sub 映射玩家並簽發 session。
    /// Android：驗證 ID Token（audience = ServerClientId）→ 同上。
    /// 任一步失敗皆拋出 GoogleAuthException，不做任何靜默降級（例如改用訪客）。
    /// </summary>
    public sealed class GoogleLoginService
    {
        private static readonly Regex CodeVerifierPattern = new("^[A-Za-z0-9._~-]{43,128}$", RegexOptions.Compiled);
        private const int MaxCodeLength = 2048;
        private const int MaxIdTokenLength = 8192;

        private readonly GoogleAuthOptions _options;
        private readonly IGoogleCodeExchanger _exchanger;
        private readonly IGoogleIdTokenValidator _validator;
        private readonly IGoogleSessionStore _sessions;

        public GoogleLoginService(
            GoogleAuthOptions options,
            IGoogleCodeExchanger exchanger,
            IGoogleIdTokenValidator validator,
            IGoogleSessionStore sessions)
        {
            _options = options;
            _exchanger = exchanger;
            _validator = validator;
            _sessions = sessions;
        }

        public async Task<GoogleSignInResult> LoginAsync(GoogleLoginInput input, CancellationToken cancellationToken)
        {
            if (!_options.IsConfigured) { throw new GoogleAuthNotConfiguredException(); }

            if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > MaxCodeLength)
            {
                throw new GoogleLoginInputException("授權碼格式不正確");
            }
            if (input.CodeVerifier == null || !CodeVerifierPattern.IsMatch(input.CodeVerifier))
            {
                throw new GoogleLoginInputException("PKCE code_verifier 格式不正確");
            }
            if (!IsLoopbackCallbackUri(input.RedirectUri))
            {
                throw new GoogleLoginInputException("redirect_uri 必須為 http://127.0.0.1 的 /callback");
            }

            string idToken = await _exchanger.ExchangeForIdTokenAsync(input.Code, input.CodeVerifier, input.RedirectUri!, cancellationToken);
            GoogleIdentity identity = await _validator.ValidateAsync(idToken, _options.ClientId, cancellationToken);
            return await _sessions.SignInAsync(identity.Subject, identity.DisplayName, input.Locale ?? string.Empty, cancellationToken);
        }

        /// <summary>
        /// Android 原生登入：客戶端（Credential Manager）已取得 Google ID Token，後端只做 audience（ServerClientId）等驗證，
        /// 不做授權碼交換，因此不需要 ClientSecret，也不接受 redirect_uri。
        /// </summary>
        public async Task<GoogleSignInResult> LoginWithIdTokenAsync(GoogleIdTokenLoginInput input, CancellationToken cancellationToken)
        {
            if (!_options.IsServerClientConfigured)
            {
                throw new GoogleAuthNotConfiguredException("Google 原生登入尚未設定 ServerClientId（Web client ID）");
            }
            if (string.IsNullOrWhiteSpace(input.IdToken) || input.IdToken.Length > MaxIdTokenLength)
            {
                throw new GoogleLoginInputException("ID Token 格式不正確");
            }

            GoogleIdentity identity = await _validator.ValidateAsync(input.IdToken, _options.ServerClientId, cancellationToken);
            return await _sessions.SignInAsync(identity.Subject, identity.DisplayName, input.Locale ?? string.Empty, cancellationToken);
        }

        /// <summary>只接受 127.0.0.1 上 /callback 的 http 回呼（僅桌面 loopback 流程；Android 不使用 loopback）。</summary>
        public static bool IsLoopbackCallbackUri(string? value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)) { return false; }
            return uri.Scheme == Uri.UriSchemeHttp
                && uri.Host == "127.0.0.1"
                && uri.AbsolutePath == "/callback"
                && string.IsNullOrEmpty(uri.Query);
        }
    }
}
