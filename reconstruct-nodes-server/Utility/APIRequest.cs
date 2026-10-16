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
                // Keep the existing boundary: body buffering and Azure transfer are not timed.
                using var timing = Environment.GetEnvironmentVariable("API_REQUEST_TIMING") == "1"
                    ? new RequestTiming() : null;
                string? timingId = timing == null ? null : Guid.NewGuid().ToString("N");
                if (timingId != null) context.Response.Headers["X-Request-Timing-Id"] = timingId;
                sw.Start();
                try
                {
                    await next(context).ConfigureAwait(false);
                }
                finally
                {
                    sw.Stop();
                    if (timing != null)
                    {
                        // Server-generated correlation only; never trust/log an incoming request ID.
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                        {
                            Kind = "RequestTiming", RequestId = timingId,
                            ElapsedMs = sw.Elapsed.TotalMilliseconds,
                            StatusCode = context.Response.StatusCode,
                            Metrics = timing.Snapshot(),
                        }));
                    }
                }

                entity = new TableEntity(nameof(APIRequest), entityId)
                {
                    { "Path", context.Request.Path.ToString() },
                    // Google 登入含授權碼與 PKCE verifier，不寫入日誌
                    { "Body", context.Request.Path.StartsWithSegments("/GoogleAuth", StringComparison.OrdinalIgnoreCase) ? "[REDACTED]" : body },
                    { "IPAddress", context.Connection.RemoteIpAddress?.ToString() },
                    { "LoadTime", sw.ElapsedMilliseconds },
                };
                Console.WriteLine($"Request to {context.Request.Path} took {sw.ElapsedMilliseconds} ms 時間:{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss.fff")}");
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
                if (entity != null)
                {
                    _ = Task.Run(async () =>
                    {
                        await azureStoreService.Transfer(TableClientType.Log, entity).ConfigureAwait(false);
                    });
                }
            }

        }
    }

}
