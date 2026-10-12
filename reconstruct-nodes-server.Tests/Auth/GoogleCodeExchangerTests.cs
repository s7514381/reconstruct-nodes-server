using System.Net;
using System.Text;
using ReconstructNodesServer.Library.Auth;

namespace ReconstructNodesServer.Tests.Auth
{
    internal sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public string? LastRequestBody { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content != null) { LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken); }
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }

    [TestClass]
    public class GoogleCodeExchangerTests
    {
        private const string Secret = "super-secret-value-do-not-leak";

        private static GoogleAuthOptions Options() => new()
        {
            ClientId = TestTokens.ClientId,
            ClientSecret = Secret,
        };

        [TestMethod]
        public async Task Success_ReturnsIdToken_AndPostsPkceAndSecretInForm()
        {
            var handler = new StubHandler(HttpStatusCode.OK, "{\"id_token\":\"header.payload.sig\",\"access_token\":\"x\"}");
            var exchanger = new GoogleCodeExchanger(new HttpClient(handler), Options());

            string idToken = await exchanger.ExchangeForIdTokenAsync("auth-code", "verifier-value", "http://127.0.0.1:5555/callback", CancellationToken.None);

            Assert.AreEqual("header.payload.sig", idToken);
            string body = handler.LastRequestBody ?? string.Empty;
            StringAssert.Contains(body, "grant_type=authorization_code");
            StringAssert.Contains(body, "code=auth-code");
            StringAssert.Contains(body, "code_verifier=verifier-value");
            StringAssert.Contains(body, "redirect_uri=http%3A%2F%2F127.0.0.1%3A5555%2Fcallback");
            StringAssert.Contains(body, "client_id=" + Uri.EscapeDataString(TestTokens.ClientId));
        }

        [TestMethod]
        public async Task InvalidGrant_MapsToAuthorizationRejected_WithoutLeakingSecret()
        {
            var handler = new StubHandler(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"error_description\":\"Bad Request\"}");
            var exchanger = new GoogleCodeExchanger(new HttpClient(handler), Options());

            GoogleAuthorizationRejectedException ex = await Assert.ThrowsExactlyAsync<GoogleAuthorizationRejectedException>(
                () => exchanger.ExchangeForIdTokenAsync("c", "v", "http://127.0.0.1/callback", CancellationToken.None));

            Assert.IsFalse(ex.Message.Contains(Secret, StringComparison.Ordinal));
        }

        [TestMethod]
        public async Task ServerError_MapsToUpstream_WithoutLeakingBodyOrSecret()
        {
            var handler = new StubHandler(HttpStatusCode.InternalServerError, "{\"error\":\"invalid_client\",\"detail\":\"" + Secret + "\"}");
            var exchanger = new GoogleCodeExchanger(new HttpClient(handler), Options());

            GoogleUpstreamException ex = await Assert.ThrowsExactlyAsync<GoogleUpstreamException>(
                () => exchanger.ExchangeForIdTokenAsync("c", "v", "http://127.0.0.1/callback", CancellationToken.None));

            Assert.IsFalse(ex.Message.Contains(Secret, StringComparison.Ordinal));
        }

        [TestMethod]
        public async Task MissingIdToken_MapsToUpstream()
        {
            var handler = new StubHandler(HttpStatusCode.OK, "{\"access_token\":\"x\"}");
            var exchanger = new GoogleCodeExchanger(new HttpClient(handler), Options());

            await Assert.ThrowsExactlyAsync<GoogleUpstreamException>(
                () => exchanger.ExchangeForIdTokenAsync("c", "v", "http://127.0.0.1/callback", CancellationToken.None));
        }

        [TestMethod]
        public async Task NonJsonBody_MapsToUpstream()
        {
            var handler = new StubHandler(HttpStatusCode.BadGateway, "<html>oops</html>");
            var exchanger = new GoogleCodeExchanger(new HttpClient(handler), Options());

            await Assert.ThrowsExactlyAsync<GoogleUpstreamException>(
                () => exchanger.ExchangeForIdTokenAsync("c", "v", "http://127.0.0.1/callback", CancellationToken.None));
        }
    }
}
