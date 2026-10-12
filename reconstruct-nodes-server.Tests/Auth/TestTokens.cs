using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ReconstructNodesServer.Library.Auth;

namespace ReconstructNodesServer.Tests.Auth
{
    /// <summary>測試用固定 RSA 金鑰與 Google 風格 ID Token 產生器；不含任何真實 Google 憑證。</summary>
    internal sealed class FakeKeySource : IGoogleSigningKeySource
    {
        private readonly IReadOnlyList<SecurityKey> _keys;

        public FakeKeySource(params SecurityKey[] keys) { _keys = keys; }

        public Task<IReadOnlyList<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken)
            => Task.FromResult(_keys);
    }

    internal static class TestTokens
    {
        public const string ClientId = "test-client-id.apps.googleusercontent.com";
        public const string ServerClientId = "test-server-client-id.apps.googleusercontent.com";
        public const string Issuer = "https://accounts.google.com";

        public static RsaSecurityKey CreateKey(string keyId)
        {
            RSA rsa = RSA.Create(2048);
            return new RsaSecurityKey(rsa) { KeyId = keyId };
        }

        public static GoogleAuthOptions CreateOptions() => new()
        {
            ClientId = ClientId,
            ClientSecret = "test-secret-not-real",
            ServerClientId = ServerClientId,
        };

        public static string CreateIdToken(
            SecurityKey? signingKey,
            string issuer = Issuer,
            string audience = ClientId,
            DateTime? expiresUtc = null,
            string? subject = "google-sub-123",
            string name = "Tester")
        {
            var claims = new List<Claim>();
            if (subject != null) { claims.Add(new Claim("sub", subject)); }
            claims.Add(new Claim("name", name));

            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = audience,
                Subject = new ClaimsIdentity(claims),
                Expires = expiresUtc ?? DateTime.UtcNow.AddMinutes(10),
                SigningCredentials = signingKey == null ? null : new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256),
            };
            return new JsonWebTokenHandler().CreateToken(descriptor);
        }
    }
}
