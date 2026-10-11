using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Filter
{
    public class AuthorizeFilter(PlayerService playerService, IMemoryCache cache) : BaseFilter
    {
        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            Guid? token = GetToken(context.HttpContext);
            if(token == null) { reject(); return; }

            if (!cache.TryGetValue(token.Value, out PlayerValidateModel? model)) 
            {
                Player? data = await playerService.GetDataByToken(token.Value);
                if (data != null)
                {
                    model = data.Map<PlayerValidateModel>();

                    cache.Set(token, model, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
                    });
                }
            }
            if (model == null) { reject(); return; }

            playerService.Player = model;
            await next().ConfigureAwait(false);

            void reject() 
            {
                context.Result = new ContentResult
                {
                    StatusCode = 401,
                    Content = "身份驗證失敗",
                    ContentType = "text/plain"
                };
            }
        }

        public static Guid? GetToken(HttpContext context) 
        {
            var headers = context.Request.Headers;
            // 優先讀取 Authorization（前端 BackendApiClient 使用），保留 PlayerToken 相容既有客戶端
            string sToken = headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(sToken)) { sToken = headers["PlayerToken"].ToString(); }
#if DEBUG
            sToken = "45C3E4A4-B53D-40F8-84C2-80AA3DB0710D";
#endif

            return Guid.TryParse(sToken, out Guid token) ? token : null;
        }

        /// <summary>
        /// OnActionExecuted – after Action execute
        /// </summary>
        public override void OnActionExecuted(ActionExecutedContext filterContext)
        {
            
        }

        //OnResultExecuting – 在執行 Action Result 之前執行
        public override void OnResultExecuting(ResultExecutingContext filterContext)
        {

        }

        ////OnResultExecuted – 在執行 Action Result 之後執行
        public override void OnResultExecuted(ResultExecutedContext filterContext)
        {

        }

    }

}
