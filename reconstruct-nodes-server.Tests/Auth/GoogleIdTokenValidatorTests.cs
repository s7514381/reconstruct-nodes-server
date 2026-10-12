using Microsoft.IdentityModel.Tokens;
using ReconstructNodesServer.Library.Auth;

namespace ReconstructNodesServer.Tests.Auth
{
    [TestClass]
    public class GoogleIdTokenValidatorTests
    {
        private static readonly RsaSecurityKey SigningKey = TestTokens.CreateKey("key-1");
        private static readonly RsaSecurityKey OtherKey = TestTokens.CreateKey("key-2");

        private static GoogleIdTokenValidator CreateValidator(params SecurityKey[] trustedKeys)
            => new(TestTokens.CreateOptions(), new FakeKeySource(trustedKeys));

        [TestMethod]
        public async Task ValidToken_ReturnsSubjectAndName()
        {
            string token = TestTokens.CreateIdToken(SigningKey, subject: "sub-abc", name: "Alice");

            GoogleIdentity identity = await CreateValidator(SigningKey).ValidateAsync(token, TestTokens.ClientId, CancellationToken.None);

            Assert.AreEqual("sub-abc", identity.Subject);
            Assert.AreEqual("Alice", identity.DisplayName);
        }

        [TestMethod]
        public async Task ValidToken_WithBareGoogleIssuer_IsAccepted()
        {
            string token = TestTokens.CreateIdToken(SigningKey, issuer: "accounts.google.com");

            GoogleIdentity identity = await CreateValidator(SigningKey).ValidateAsync(token, TestTokens.ClientId, CancellationToken.None);

            Assert.AreEqual("google-sub-123", identity.Subject);
        }

        [TestMethod]
        public async Task WrongAudience_IsRejected()
        {
            string token = TestTokens.CreateIdToken(SigningKey, audience: "another-app.apps.googleusercontent.com");

            await Assert.ThrowsExactlyAsync<GoogleIdTokenInvalidException>(
                () => CreateValidator(SigningKey).ValidateAsync(token, TestTokens.ClientId, CancellationToken.None));
        }

        [TestMethod]
        public async Task WrongIssuer_IsRejected()
        {
            string token = TestTokens.CreateIdToken(SigningKey, issuer: "https://evil.example.com");

            await Assert.ThrowsExactlyAsync<GoogleIdTokenInvalidException>(
                () => CreateValidator(SigningKey).ValidateAsync(token, TestTokens.ClientId, CancellationToken.None));
        }

        [TestMethod]
        public async Task Expired_IsRejected()
        {
            string token = TestTokens.CreateIdToken(SigningKey, expiresUtc: DateTime.UtcNow.AddMinutes(-10));

            await Assert.ThrowsExactlyAsync<GoogleIdTokenInvalidException>(
                () => CreateValidator(SigningKey).ValidateAsync(token, TestTokens.ClientId, CancellationToken.None));
        }

        [TestMethod]
        public async Task SignedWithUntrustedKey_IsRejected()
        {
            string token = TestTokens.CreateIdToken(OtherKey);

            await Assert.ThrowsExactlyAsync<GoogleIdTokenInvalidException>(
                () => CreateValidator(SigningKey).ValidateAsync(token, TestTokens.ClientId, CancellationToken.None));
        }

        [TestMethod]
        public async Task MissingSubject_IsRejected()
        {
            string token = TestTokens.CreateIdToken(SigningKey, subject: null);

            await Assert.ThrowsExactlyAsync<GoogleIdTokenInvalidException>(
                () => CreateValidator(SigningKey).ValidateAsync(token, TestTokens.ClientId, CancellationToken.None));
        }

        [TestMethod]
        public async Task Unsigned_IsRejected()
        {
            string token = TestTokens.CreateIdToken(signingKey: null);

            await Assert.ThrowsExactlyAsync<GoogleIdTokenInvalidException>(
                () => CreateValidator(SigningKey).ValidateAsync(token, TestTokens.ClientId, CancellationToken.None));
        }

        [TestMethod]
        public async Task EmptyToken_IsRejected()
        {
            await Assert.ThrowsExactlyAsync<GoogleIdTokenInvalidException>(
                () => CreateValidator(SigningKey).ValidateAsync("  ", TestTokens.ClientId, CancellationToken.None));
        }

        [TestMethod]
        public async Task GarbageToken_IsRejected()
        {
            await Assert.ThrowsExactlyAsync<GoogleIdTokenInvalidException>(
                () => CreateValidator(SigningKey).ValidateAsync("not-a-jwt", TestTokens.ClientId, CancellationToken.None));
        }
    }
}
