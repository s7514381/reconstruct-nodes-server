using Microsoft.AspNetCore.Diagnostics;
using ReconstructNodesServer.Library.Models;
using System.Text.Json;

namespace ReconstructNodesServer.Utility;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled API error at {Path}", context.Request.Path);
        if (context.Response.HasStarted) { return false; }
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(
            BackendApiResult<object>.Create(500, ApiMessageKeys.ErrorUnexpected),
            new JsonSerializerOptions { PropertyNamingPolicy = null },
            cancellationToken);
        return true;
    }
}
