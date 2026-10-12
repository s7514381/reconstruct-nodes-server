#nullable enable
namespace ReconstructNodesServer.Library.Auth
{
    /// <summary>
    /// Google 登入設定，對應 appsettings 的 GoogleAuth 區段。
    /// ClientSecret 只允許來自 user-secrets 或環境變數（GoogleAuth__ClientSecret），不得寫入 appsettings、git 或客戶端。
    /// </summary>
    public class GoogleAuthOptions
    {
        public const string SectionName = "GoogleAuth";

        /// <summary>桌面 OAuth Client ID（公開值）；桌面授權碼流程的 ID Token audience。</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// Web（server）OAuth Client ID；Android Credential Manager 的 setServerClientId 會讓 ID Token 的 audience 為此值。
        /// 只用於 LoginIdToken，不需要 ClientSecret。
        /// </summary>
        public string ServerClientId { get; set; } = string.Empty;

        /// <summary>OAuth Client Secret，僅後端於授權碼交換時使用。</summary>
        public string ClientSecret { get; set; } = string.Empty;

        public string TokenEndpoint { get; set; } = "https://oauth2.googleapis.com/token";

        public string JwksEndpoint { get; set; } = "https://www.googleapis.com/oauth2/v3/certs";

        /// <summary>ID Token 時間驗證允許的時鐘偏移（秒）。</summary>
        public int ClockSkewSeconds { get; set; } = 60;

        /// <summary>Google 簽章公鑰快取時間（分鐘）。</summary>
        public int JwksCacheMinutes { get; set; } = 60;

        /// <summary>ClientId 與 ClientSecret 皆已設定時才啟用 Google 登入。</summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

        /// <summary>ServerClientId 已設定時才啟用原生 ID Token 登入（Android）。</summary>
        public bool IsServerClientConfigured => !string.IsNullOrWhiteSpace(ServerClientId);
    }
}
