using ReconstructNodesServer.Library.Auth;

namespace ReconstructNodesServer.Tests.Auth
{
    internal sealed class FakeExchanger : IGoogleCodeExchanger
    {
        public int CallCount { get; private set; }
        public Exception? ThrowOnCall { get; set; }
        public string IdToken { get; set; } = "id-token";

        public Task<string> ExchangeForIdTokenAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken)
        {
            CallCount++;
            if (ThrowOnCall != null) { throw ThrowOnCall; }
            return Task.FromResult(IdToken);
        }
    }

    internal sealed class FakeValidator : IGoogleIdTokenValidator
    {
        public int CallCount { get; private set; }
        public string? LastAudience { get; private set; }
        public string? LastIdToken { get; private set; }
        public Exception? ThrowOnCall { get; set; }
        public GoogleIdentity Identity { get; set; } = new("google-sub-123", "Tester");

        public Task<GoogleIdentity> ValidateAsync(string idToken, string audience, CancellationToken cancellationToken)
        {
            CallCount++;
            LastAudience = audience;
            LastIdToken = idToken;
            if (ThrowOnCall != null) { throw ThrowOnCall; }
            return Task.FromResult(Identity);
        }
    }

    internal sealed class FakeSessionStore : IGoogleSessionStore
    {
        public int CallCount { get; private set; }
        public string? LastSubject { get; private set; }
        public string? LastName { get; private set; }

        public Task<GoogleSignInResult> SignInAsync(string googleSubject, string displayName, string locale, CancellationToken cancellationToken)
        {
            CallCount++;
            LastSubject = googleSubject;
            LastName = displayName;
            return Task.FromResult(new GoogleSignInResult(Guid.Parse("11111111-2222-3333-4444-555555555555"), displayName));
        }
    }

    [TestClass]
    public class GoogleLoginServiceTests
    {
        private const string ValidVerifier = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"; // 43 chars
        private const string ValidRedirect = "http://127.0.0.1:53123/callback";

        private static GoogleLoginInput ValidInput() => new("auth-code", ValidVerifier, ValidRedirect, "zh-TW");

        private static (GoogleLoginService service, FakeExchanger exchanger, FakeValidator validator, FakeSessionStore store)
            Create(GoogleAuthOptions? options = null)
        {
            var exchanger = new FakeExchanger();
            var validator = new FakeValidator();
            var store = new FakeSessionStore();
            var service = new GoogleLoginService(options ?? TestTokens.CreateOptions(), exchanger, validator, store);
            return (service, exchanger, validator, store);
        }

        [TestMethod]
        public async Task NotConfigured_Throws_AndNeverCallsGoogleOrStore()
        {
            var (service, exchanger, _, store) = Create(new GoogleAuthOptions { ClientId = "", ClientSecret = "" });

            await Assert.ThrowsExactlyAsync<GoogleAuthNotConfiguredException>(
                () => service.LoginAsync(ValidInput(), CancellationToken.None));

            Assert.AreEqual(0, exchanger.CallCount);
            Assert.AreEqual(0, store.CallCount);
        }

        [TestMethod]
        public async Task ClientIdWithoutSecret_IsNotConfigured()
        {
            var (service, exchanger, _, _) = Create(new GoogleAuthOptions { ClientId = "id", ClientSecret = "" });

            await Assert.ThrowsExactlyAsync<GoogleAuthNotConfiguredException>(
                () => service.LoginAsync(ValidInput(), CancellationToken.None));
            Assert.AreEqual(0, exchanger.CallCount);
        }

        [TestMethod]
        public async Task ShortVerifier_IsInputError()
        {
            var (service, exchanger, _, store) = Create();

            await Assert.ThrowsExactlyAsync<GoogleLoginInputException>(
                () => service.LoginAsync(ValidInput() with { CodeVerifier = "short" }, CancellationToken.None));
            Assert.AreEqual(0, exchanger.CallCount);
            Assert.AreEqual(0, store.CallCount);
        }

        [TestMethod]
        public async Task MissingCode_IsInputError()
        {
            var (service, exchanger, _, _) = Create();

            await Assert.ThrowsExactlyAsync<GoogleLoginInputException>(
                () => service.LoginAsync(ValidInput() with { Code = "" }, CancellationToken.None));
            Assert.AreEqual(0, exchanger.CallCount);
        }

        [TestMethod]
        [DataRow("https://127.0.0.1/callback")]
        [DataRow("http://localhost:5000/callback")]
        [DataRow("http://127.0.0.1/other")]
        [DataRow("http://127.0.0.1/callback?x=1")]
        [DataRow("http://evil.example/callback")]
        [DataRow(null)]
        public async Task BadRedirect_IsInputError(string? redirect)
        {
            var (service, exchanger, _, _) = Create();

            await Assert.ThrowsExactlyAsync<GoogleLoginInputException>(
                () => service.LoginAsync(ValidInput() with { RedirectUri = redirect }, CancellationToken.None));
            Assert.AreEqual(0, exchanger.CallCount);
        }

        [TestMethod]
        public async Task ExchangeRejected_Throws_AndStoreNotCalled()
        {
            var (service, exchanger, _, store) = Create();
            exchanger.ThrowOnCall = new GoogleAuthorizationRejectedException("cancelled");

            await Assert.ThrowsExactlyAsync<GoogleAuthorizationRejectedException>(
                () => service.LoginAsync(ValidInput(), CancellationToken.None));
            Assert.AreEqual(0, store.CallCount);
        }

        [TestMethod]
        public async Task InvalidIdToken_Throws_AndStoreNotCalled()
        {
            var (service, _, validator, store) = Create();
            validator.ThrowOnCall = new GoogleIdTokenInvalidException("bad");

            await Assert.ThrowsExactlyAsync<GoogleIdTokenInvalidException>(
                () => service.LoginAsync(ValidInput(), CancellationToken.None));
            Assert.AreEqual(0, store.CallCount);
        }

        [TestMethod]
        public async Task Success_MapsValidatedSubjectToSession()
        {
            var (service, _, validator, store) = Create();
            validator.Identity = new GoogleIdentity("google-sub-999", "Bob");

            GoogleSignInResult result = await service.LoginAsync(ValidInput(), CancellationToken.None);

            Assert.AreEqual(1, store.CallCount);
            Assert.AreEqual("google-sub-999", store.LastSubject);
            Assert.AreEqual("Bob", store.LastName);
            Assert.AreEqual("Bob", result.Name);
            Assert.AreEqual(Guid.Parse("11111111-2222-3333-4444-555555555555"), result.Token);
        }

        [TestMethod]
        [DataRow("http://127.0.0.1/callback", true)]
        [DataRow("http://127.0.0.1:8080/callback", true)]
        [DataRow("https://127.0.0.1/callback", false)]
        [DataRow("http://127.0.0.1/callback?a=b", false)]
        [DataRow("http://localhost/callback", false)]
        [DataRow("not a uri", false)]
        public void IsLoopbackCallbackUri_OnlyAcceptsLoopbackCallback(string value, bool expected)
        {
            Assert.AreEqual(expected, GoogleLoginService.IsLoopbackCallbackUri(value));
        }

        [TestMethod]
        public async Task Desktop_ValidatesIdTokenWithDesktopClientIdAudience()
        {
            var (service, _, validator, _) = Create();

            await service.LoginAsync(ValidInput(), CancellationToken.None);

            Assert.AreEqual(TestTokens.ClientId, validator.LastAudience);
        }

        private static GoogleIdTokenLoginInput ValidIdTokenInput() => new("header.payload.signature", "zh-TW");

        [TestMethod]
        public async Task IdToken_ServerClientIdMissing_Throws_AndNeverValidatesOrStores()
        {
            var (service, _, validator, store) = Create(new GoogleAuthOptions { ClientId = "id", ClientSecret = "s", ServerClientId = "" });

            await Assert.ThrowsExactlyAsync<GoogleAuthNotConfiguredException>(
                () => service.LoginWithIdTokenAsync(ValidIdTokenInput(), CancellationToken.None));
            Assert.AreEqual(0, validator.CallCount);
            Assert.AreEqual(0, store.CallCount);
        }

        [TestMethod]
        public async Task IdToken_DoesNotRequireClientSecret()
        {
            var (service, _, validator, store) = Create(new GoogleAuthOptions { ServerClientId = TestTokens.ServerClientId, ClientSecret = "" });

            GoogleSignInResult result = await service.LoginWithIdTokenAsync(ValidIdTokenInput(), CancellationToken.None);

            Assert.AreEqual(1, store.CallCount);
            Assert.AreEqual(TestTokens.ServerClientId, validator.LastAudience);
            Assert.AreEqual("Tester", result.Name);
        }

        [TestMethod]
        public async Task IdToken_UsesServerClientIdAudience_NotDesktopClientId()
        {
            var (service, exchanger, validator, _) = Create();

            await service.LoginWithIdTokenAsync(ValidIdTokenInput(), CancellationToken.None);

            Assert.AreEqual(TestTokens.ServerClientId, validator.LastAudience);
            Assert.AreNotEqual(TestTokens.ClientId, validator.LastAudience);
            Assert.AreEqual(0, exchanger.CallCount, "Android 原生登入不得進行授權碼交換");
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow(null)]
        public async Task IdToken_EmptyInput_IsInputError(string? idToken)
        {
            var (service, _, validator, store) = Create();

            await Assert.ThrowsExactlyAsync<GoogleLoginInputException>(
                () => service.LoginWithIdTokenAsync(new GoogleIdTokenLoginInput(idToken, "zh-TW"), CancellationToken.None));
            Assert.AreEqual(0, validator.CallCount);
            Assert.AreEqual(0, store.CallCount);
        }

        [TestMethod]
        public async Task IdToken_Oversized_IsInputError()
        {
            var (service, _, validator, _) = Create();

            await Assert.ThrowsExactlyAsync<GoogleLoginInputException>(
                () => service.LoginWithIdTokenAsync(new GoogleIdTokenLoginInput(new string('a', 8193), "zh-TW"), CancellationToken.None));
            Assert.AreEqual(0, validator.CallCount);
        }

        [TestMethod]
        public async Task IdToken_InvalidToken_Throws_AndStoreNotCalled()
        {
            var (service, _, validator, store) = Create();
            validator.ThrowOnCall = new GoogleIdTokenInvalidException("bad");

            await Assert.ThrowsExactlyAsync<GoogleIdTokenInvalidException>(
                () => service.LoginWithIdTokenAsync(ValidIdTokenInput(), CancellationToken.None));
            Assert.AreEqual(0, store.CallCount);
        }

        [TestMethod]
        public async Task IdToken_Success_MapsValidatedSubjectToSession()
        {
            var (service, _, validator, store) = Create();
            validator.Identity = new GoogleIdentity("google-sub-777", "Carol");

            GoogleSignInResult result = await service.LoginWithIdTokenAsync(ValidIdTokenInput(), CancellationToken.None);

            Assert.AreEqual("google-sub-777", store.LastSubject);
            Assert.AreEqual("Carol", result.Name);
        }
    }
}
