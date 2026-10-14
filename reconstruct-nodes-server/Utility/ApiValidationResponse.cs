using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Utility;

public static class ApiValidationResponse
{
    public static IActionResult Create(ActionContext context)
    {
        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("ApiValidation").LogWarning("Invalid API request at {Path}", context.HttpContext.Request.Path);
        return new BadRequestObjectResult(BackendApiResult<object>.Create(400, ApiMessageKeys.RequestInvalid));
    }
}
