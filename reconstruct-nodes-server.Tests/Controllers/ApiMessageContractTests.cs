using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Controllers;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Utility;
using ReconstructNodesServer.Services;
using ReconstructNodesServer.Tests.Services;
using Microsoft.Extensions.Caching.Memory;
using ReconstructNodesServer.Library.Auth;
using ReconstructNodesServer.Tests.Auth;

namespace ReconstructNodesServer.Tests.Controllers;

[TestClass]
public class ApiMessageContractTests
{
    [TestMethod]
    public void Envelope_HasOnlyNewFields_AndCopiesNamedArguments()
    {
        Dictionary<string, string> arguments = new() { ["cost"] = "100" };
        var result = BackendApiResult<object>.Create(400,
            new ApiMessage(ApiMessageKeys.EconomyInsufficientGold, arguments));
        arguments["cost"] = "200";
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        CollectionAssert.AreEquivalent(
            new[] { "Status", "Success", "MessageKey", "MessageArgs", "Data" },
            json.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual("100", json.RootElement.GetProperty("MessageArgs").GetProperty("cost").GetString());
        Assert.IsFalse(result.Success);
        var success = BackendApiResult<object>.Create(200);
        Assert.IsTrue(success.Success);
        Assert.IsEmpty(success.MessageKey);
        Assert.IsEmpty(success.MessageArgs);
    }

    [TestMethod]
    public void TransactionFailure_Remains400_WithGenericFallback()
    {
        var controller = new FailureProbeController();
        var result = (BadRequestObjectResult)controller.Fail(null);
        Assert.AreEqual(400, result.StatusCode);
        Assert.AreEqual(ApiMessageKeys.ErrorUnexpected, ((BackendApiResult<object>)result.Value!).MessageKey);
        result = (BadRequestObjectResult)controller.Fail(new ApiMessage(ApiMessageKeys.ItemInsufficient));
        Assert.AreEqual(ApiMessageKeys.ItemInsufficient, ((BackendApiResult<object>)result.Value!).MessageKey);
    }

    [TestMethod]
    public async Task ManagerMissingCredentials_Is400KeyEnvelope()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = new ManagerController(null!, cache);
        var result = (ObjectResult)await controller.Login(new ManagerLoginRequest());
        Assert.AreEqual(400, result.StatusCode);
        Assert.AreEqual(ApiMessageKeys.AuthCredentialsRequired,
            ((BackendApiResult<BackendLoginData>)result.Value!).MessageKey);
    }

    [TestMethod]
    public async Task GoogleInvalidInput_DoesNotExposeExceptionText()
    {
        var service = new GoogleLoginService(TestTokens.CreateOptions(),
            new FakeExchanger(), new FakeValidator(), new FakeSessionStore());
        var controller = new GoogleAuthController(new BaseApiArgument(null!, NullLoggerFactory.Instance), service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var result = (ObjectResult)await controller.Login(new GoogleLoginRequest());
        Assert.AreEqual(400, result.StatusCode);
        var envelope = (BackendApiResult<BackendLoginData>)result.Value!;
        Assert.AreEqual(ApiMessageKeys.AuthGoogleInputInvalid, envelope.MessageKey);
        Assert.IsEmpty(envelope.MessageArgs);
        Assert.IsFalse(envelope.Success);
    }

    [TestMethod]
    [DataRow("GET")]
    [DataRow("POST")]
    public async Task UnhandledException_Is500Envelope_WithoutExceptionText(string method)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance);
        Assert.IsTrue(await handler.TryHandleAsync(context, new InvalidOperationException("private diagnostic"), default));
        Assert.AreEqual(500, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        string body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("private diagnostic", body);
        using var json = JsonDocument.Parse(body);
        Assert.AreEqual(ApiMessageKeys.ErrorUnexpected, json.RootElement.GetProperty("MessageKey").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("Success").GetBoolean());
        Assert.IsFalse(json.RootElement.TryGetProperty("Message", out _));
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{")]
    public async Task MvcValidation_AndMalformedJson_Use400Envelope(string body)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(ValidationProbeController).Assembly)
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ApiValidationResponse.Create)
            .AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = null);
        await using var web = builder.Build();
        var app = new ApplicationBuilder(web.Services);
        app.UseRouting();
        app.UseEndpoints(endpoints => endpoints.MapControllers());
        var context = new DefaultHttpContext { RequestServices = web.Services };
        context.Request.Method = "POST";
        context.Request.Path = "/message-contract-validation";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentLength = context.Request.Body.Length;
        context.Response.Body = new MemoryStream();
        await app.Build()(context);
        Assert.AreEqual(400, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.AreEqual(ApiMessageKeys.RequestInvalid, json.RootElement.GetProperty("MessageKey").GetString());
        Assert.IsFalse(json.RootElement.TryGetProperty("Message", out _));
    }

    private sealed class FailureProbeController() : BaseApiController(new BaseApiArgument(null!, NullLoggerFactory.Instance))
    {
        public IActionResult Fail(ApiMessage? message) => TransactionFailure(message);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TransactionBoundary_MapsOnlyTypedBusinessErrors(bool business)
    {
        var (argument, _) = SnapshotSaveTests.CreateArgument();
        argument.LazyServiceArgument.BaseService = new Lazy<BaseService>(() => new FailedTransactionService(argument, business));
        var controller = new TransactionProbeController(new BaseApiArgument(argument.LazyServiceArgument, NullLoggerFactory.Instance));
        var result = (BadRequestObjectResult)await controller.Execute();
        var envelope = (BackendApiResult<object>)result.Value!;
        Assert.AreEqual(400, result.StatusCode);
        Assert.AreEqual(business ? ApiMessageKeys.EconomyInsufficientGold : ApiMessageKeys.ErrorUnexpected, envelope.MessageKey);
        Assert.DoesNotContain("private diagnostic", JsonSerializer.Serialize(envelope));
        if (business) { Assert.AreEqual("100", envelope.MessageArgs["cost"]); }
    }

    private sealed class TransactionProbeController(BaseApiArgument argument) : BaseApiController(argument)
    {
        public async Task<IActionResult> Execute()
        {
            var (success, message) = await TransactionFunc(() => Task.CompletedTask);
            return success ? Ok() : TransactionFailure(message);
        }
    }

    private sealed class FailedTransactionService(BaseServiceArgument argument, bool business) : BaseService(argument)
    {
        public override Task<(bool, Exception?)> TransactionFunc(Func<Task> func, Func<Task>? exceptionFunc = null)
        {
            Exception error = business
                ? new BusinessOperationException(new ApiMessage(ApiMessageKeys.EconomyInsufficientGold,
                    new Dictionary<string, string> { ["cost"] = "100" }))
                : new InvalidOperationException("private diagnostic");
            return Task.FromResult<(bool, Exception?)>((false, error));
        }
    }
}

[ApiController]
public sealed class ValidationProbeController : ControllerBase
{
    [HttpPost("/message-contract-validation")]
    public IActionResult Validate(ValidationProbeRequest request) => Ok();
}

public sealed class ValidationProbeRequest
{
    [Required]
    public string? Name { get; set; }
}
