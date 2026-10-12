#nullable enable
using System;

namespace ReconstructNodesServer.Library.Auth
{
    /// <summary>Google 登入流程的可預期錯誤；控制器依子類型對應 HTTP 狀態，不回傳內部細節或 Token。</summary>
    public abstract class GoogleAuthException : Exception
    {
        protected GoogleAuthException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>未設定 ClientId 或 ClientSecret（503）。</summary>
    public sealed class GoogleAuthNotConfiguredException : GoogleAuthException
    {
        public GoogleAuthNotConfiguredException(string message = "Google 登入尚未設定 ClientId 與 ClientSecret") : base(message) { }
    }

    /// <summary>請求參數格式不正確（400）。</summary>
    public sealed class GoogleLoginInputException : GoogleAuthException
    {
        public GoogleLoginInputException(string message) : base(message) { }
    }

    /// <summary>授權碼無效、已過期、已使用，或 PKCE/redirect_uri 不符（401）。</summary>
    public sealed class GoogleAuthorizationRejectedException : GoogleAuthException
    {
        public GoogleAuthorizationRejectedException(string message) : base(message) { }
    }

    /// <summary>ID Token 的簽章、issuer、audience、有效期或 sub 驗證失敗（401）。</summary>
    public sealed class GoogleIdTokenInvalidException : GoogleAuthException
    {
        public GoogleIdTokenInvalidException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>連線 Google 端點失敗、回應異常、或設定的 client 憑證被拒（502）。</summary>
    public sealed class GoogleUpstreamException : GoogleAuthException
    {
        public GoogleUpstreamException(string message, Exception? inner = null) : base(message, inner) { }
    }
}
