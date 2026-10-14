using Azure.Data.Tables;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using ReconstructNodesServer.Library.GameModel;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ReconstructNodesServer.Filter;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Utility
{
    public class APIRequest(RequestDelegate next, 
        AzureStoreService azureStoreService,
        IMemoryCache cache)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            // 如果不是 POST 請求，直接跳過處理
            if (!context.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await next(context).ConfigureAwait(false); return;
            }

            TableEntity? entity = null;
            string entityId = DateTime.Now.ToString("yyyyMMddHHmmss") + BaseFunction.GetVerificationCode();
            try
            {
                // 確保 HTTP Request 可以多次讀取
                context.Request.EnableBuffering();

                string body = "";
                using (var bodyReader = new
                    StreamReader(stream: context.Request.Body,
                                 encoding: Encoding.UTF8,
                                 detectEncodingFromByteOrderMarks: false,
                                 bufferSize: 1024,
                                 leaveOpen: true))
                {
                    body = await bodyReader.ReadToEndAsync().ConfigureAwait(false);
                }

                // 將 HTTP Request 的 Stream 起始位置歸零
                context.Request.Body.Position = 0;

                var sw = new Stopwatch();
                sw.Start();
                await next(context).ConfigureAwait(false);
                sw.Stop();

                entity = new TableEntity(nameof(APIRequest), entityId)
                {
                    { "Path", context.Request.Path.ToString() },
                    // Google 登入含授權碼與 PKCE verifier，不寫入日誌
                    { "Body", context.Request.Path.StartsWithSegments("/GoogleAuth", StringComparison.OrdinalIgnoreCase) ? "[REDACTED]" : body },
                    { "IPAddress", context.Connection.RemoteIpAddress?.ToString() },
                    { "LoadTime", sw.ElapsedMilliseconds },
                };
            }
            catch (Exception ex)
            {
                entity = new TableEntity($"{nameof(APIRequest)}_Error", entityId)
                {
                    { "ErrorMessage", ex.ToString() },
                };
                // 重新拋出，避免例外被吞掉後回傳空的 200
                throw;
            }
            finally 
            {
                Guid? token = AuthorizeFilter.GetToken(context);
                if (entity != null
                    && token.HasValue)
                {
                    if (cache.TryGetValue(token.Value, out PlayerValidateModel? model)) { entity.Add("CreateUserId", model?.Id); }
                    // 快取未命中時不得寫入原始 session Token（Azure 日誌可讀），只記錄遮蔽標記
                    else { entity.Add("Token", "[REDACTED]"); }
                }
                _ = Task.Run(async () =>
                {
                    await azureStoreService.Transfer(TableClientType.Log, entity).ConfigureAwait(false);
                });
            }

        }
    }

}
