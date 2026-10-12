#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;

namespace ReconstructNodesServer.Library.Auth
{
    /// <summary>提供 Google ID Token 驗證用的簽章公鑰。</summary>
    public interface IGoogleSigningKeySource
    {
        Task<IReadOnlyList<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken);
    }

    /// <summary>從 Google JWKS 端點取得公鑰並快取，快取過期後重新取得。</summary>
    public sealed class GoogleSigningKeySource : IGoogleSigningKeySource
    {
        private sealed record CachedKeys(IReadOnlyList<SecurityKey> Keys, DateTime ExpiresAtUtc);

        private readonly HttpClient _httpClient;
        private readonly GoogleAuthOptions _options;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private volatile CachedKeys? _cache;

        public GoogleSigningKeySource(HttpClient httpClient, GoogleAuthOptions options)
        {
            _httpClient = httpClient;
            _options = options;
        }

        public async Task<IReadOnlyList<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken)
        {
            CachedKeys? cached = _cache;
            if (cached != null && DateTime.UtcNow < cached.ExpiresAtUtc) { return cached.Keys; }

            await _gate.WaitAsync(cancellationToken);
            try
            {
                cached = _cache;
                if (cached != null && DateTime.UtcNow < cached.ExpiresAtUtc) { return cached.Keys; }

                IReadOnlyList<SecurityKey> keys = await FetchAsync(cancellationToken);
                _cache = new CachedKeys(keys, DateTime.UtcNow.AddMinutes(_options.JwksCacheMinutes));
                return keys;
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<IReadOnlyList<SecurityKey>> FetchAsync(CancellationToken cancellationToken)
        {
            string json;
            try
            {
                json = await _httpClient.GetStringAsync(_options.JwksEndpoint, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new GoogleUpstreamException("無法取得 Google 簽章金鑰", ex);
            }

            List<SecurityKey> keys;
            try
            {
                keys = new JsonWebKeySet(json).GetSigningKeys().ToList();
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException or FormatException)
            {
                throw new GoogleUpstreamException("Google 簽章金鑰格式異常", ex);
            }

            if (keys.Count == 0) { throw new GoogleUpstreamException("Google 簽章金鑰為空"); }
            return keys;
        }
    }
}
