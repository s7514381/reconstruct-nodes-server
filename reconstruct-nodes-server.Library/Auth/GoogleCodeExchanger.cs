#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ReconstructNodesServer.Library.Auth
{
    public interface IGoogleCodeExchanger
    {
        /// <summary>以授權碼與 PKCE verifier 向 Google 換取 ID Token（client secret 只在後端使用）。</summary>
        Task<string> ExchangeForIdTokenAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Google OAuth 授權碼交換。請求與錯誤回應皆不記錄 code、verifier、client secret 或 id_token。
    /// </summary>
    public sealed class GoogleCodeExchanger : IGoogleCodeExchanger
    {
        private readonly HttpClient _httpClient;
        private readonly GoogleAuthOptions _options;

        public GoogleCodeExchanger(HttpClient httpClient, GoogleAuthOptions options)
        {
            _httpClient = httpClient;
            _options = options;
        }

        public async Task<string> ExchangeForIdTokenAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken)
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
            });

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsync(_options.TokenEndpoint, form, cancellationToken);
            }
            catch (Exception ex) when (IsNetworkFailure(ex, cancellationToken))
            {
                throw new GoogleUpstreamException("無法連線 Google 授權端點", ex);
            }

            using (response)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode) { throw MapFailure(response.StatusCode, body); }

                return ReadIdToken(body);
            }
        }

        private static bool IsNetworkFailure(Exception ex, CancellationToken cancellationToken)
        {
            if (ex is HttpRequestException) { return true; }
            // HttpClient 逾時會以 TaskCanceledException 呈現；呼叫端取消則不視為上游失敗
            return ex is TaskCanceledException && !cancellationToken.IsCancellationRequested;
        }

        private static GoogleAuthException MapFailure(HttpStatusCode status, string body)
        {
            // invalid_grant：授權碼過期、已使用、redirect_uri 或 PKCE 不符，屬使用者需重新授權
            if (TryReadString(body, "error") == "invalid_grant")
            {
                return new GoogleAuthorizationRejectedException("Google 授權已取消、過期或已使用，請重新登入");
            }
            // invalid_client 等代表伺服器端設定問題，不對使用者暴露細節
            return new GoogleUpstreamException($"Google 授權端點回應失敗（HTTP {(int)status}）");
        }

        private static string ReadIdToken(string body)
        {
            string? idToken = TryReadString(body, "id_token");
            if (string.IsNullOrWhiteSpace(idToken)) { throw new GoogleUpstreamException("Google 回應缺少 id_token"); }
            return idToken;
        }

        private static string? TryReadString(string json, string property)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty(property, out JsonElement value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
