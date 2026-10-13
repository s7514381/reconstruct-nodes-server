using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using ReconstructNodesServer.Filter;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Tests.Auth
{
    [TestClass]
    public class AuthorizeFilterTests
    {
        private static DefaultHttpContext CreateContext(params (string Key, string Value)[] headers)
        {
            var context = new DefaultHttpContext();
            foreach (var (key, value) in headers) { context.Request.Headers[key] = value; }
            return context;
        }

        [TestMethod]
        public void NoToken_ReturnsNull_WithoutDebugFallback()
        {
            Assert.IsNull(AuthorizeFilter.GetToken(CreateContext()));
        }

        [TestMethod]
        public void AuthorizationHeader_IsParsed()
        {
            var guid = Guid.NewGuid();
            Assert.AreEqual(guid, AuthorizeFilter.GetToken(CreateContext(("Authorization", guid.ToString()))));
        }

        [TestMethod]
        public void PlayerTokenHeader_IsCompatibilityFallback()
        {
            var guid = Guid.NewGuid();
            Assert.AreEqual(guid, AuthorizeFilter.GetToken(CreateContext(("PlayerToken", guid.ToString()))));
        }

        [TestMethod]
        public void MalformedToken_ReturnsNull()
        {
            Assert.IsNull(AuthorizeFilter.GetToken(CreateContext(("Authorization", "not-a-guid"))));
        }

        [TestMethod]
        public async Task Anonymous_IsRejectedWith401_AndActionNotExecuted()
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var filter = new AuthorizeFilter(null!, cache);
            var httpContext = CreateContext();
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            var executing = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());

            bool nextCalled = false;
            await filter.OnActionExecutionAsync(executing, () =>
            {
                nextCalled = true;
                return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
            });

            Assert.IsFalse(nextCalled);
            var result = executing.Result as ObjectResult;
            Assert.IsNotNull(result);
            Assert.AreEqual(401, result.StatusCode);
            var envelope = result.Value as BackendApiResult<object>;
            Assert.IsNotNull(envelope);
            Assert.AreEqual(ApiMessageKeys.AuthInvalid, envelope.MessageKey);
        }
    }
}
