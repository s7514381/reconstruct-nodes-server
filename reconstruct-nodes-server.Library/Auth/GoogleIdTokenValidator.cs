#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ReconstructNodesServer.Library.Auth
{
    /// <summary>已通過簽章與 claims 驗證的 Google 身分。Subject 即 Google sub，為玩家映射的唯一鍵。</summary>
    public sealed record GoogleIdentity(string Subject, string DisplayName);

    public interface IGoogleIdTokenValidator
    {
        /// <summary>驗證 ID Token；audience 由呼叫端明確指定（桌面 ClientId 或 Android ServerClientId）。</summary>
        Task<GoogleIdentity> ValidateAsync(string idToken, string audience, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 驗證 Google ID Token：RS256 簽章（JWKS）、issuer、audience（呼叫端指定）、有效期與 sub。
    /// 客戶端提供的任何 GoogleId / email 皆不被信任，只採用此處驗證後的 sub。
    /// </summary>
    public sealed class GoogleIdTokenValidator : IGoogleIdTokenValidator
    {
        private static readonly string[] ValidIssuers = { "accounts.google.com", "https://accounts.google.com" };

        private readonly GoogleAuthOptions _options;
        private readonly IGoogleSigningKeySource _keySource;
        private readonly JsonWebTokenHandler _handler = new();

        public GoogleIdTokenValidator(GoogleAuthOptions options, IGoogleSigningKeySource keySource)
        {
            _options = options;
            _keySource = keySource;
        }

        public async Task<GoogleIdentity> ValidateAsync(string idToken, string audience, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(idToken)) { throw new GoogleIdTokenInvalidException("ID Token 為空"); }
            if (string.IsNullOrWhiteSpace(audience)) { throw new GoogleAuthNotConfiguredException("Google audience 未設定"); }

            IReadOnlyList<SecurityKey> keys = await _keySource.GetSigningKeysAsync(cancellationToken);
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = ValidIssuers,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromSeconds(_options.ClockSkewSeconds),
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = keys,
                RequireSignedTokens = true,
                ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
            };

            TokenValidationResult result = await _handler.ValidateTokenAsync(idToken, parameters);
            if (!result.IsValid) { throw new GoogleIdTokenInvalidException("Google ID Token 驗證失敗", result.Exception); }
            if (result.SecurityToken is not JsonWebToken jwt) { throw new GoogleIdTokenInvalidException("Google ID Token 格式異常"); }
            if (string.IsNullOrWhiteSpace(jwt.Subject)) { throw new GoogleIdTokenInvalidException("Google ID Token 缺少 sub"); }

            jwt.TryGetPayloadValue("name", out string? name);
            return new GoogleIdentity(jwt.Subject, name ?? string.Empty);
        }
    }
}
