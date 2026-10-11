using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json;
using System.Text;
using ReconstructNodesServer.Utility;

namespace ReconstructNodesServer.Filter
{
    public class ApiFilter : BaseFilter
    {
        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
#if DEBUG
            await next().ConfigureAwait(false);
            return;
#endif
            //驗證是自己的前端打來的
            var headers = context.HttpContext.Request.Headers;
            string encodeString = headers["Encode"].ToString();
            if (!Encode.Validate(encodeString, 60)) { reject(); return; }

            await next().ConfigureAwait(false);

            void reject()
            {
                context.Result = new ContentResult
                {
                    StatusCode = 401,
                    Content = "加密驗證失敗",
                    ContentType = "text/plain"
                };
            }
        }

    }
}
