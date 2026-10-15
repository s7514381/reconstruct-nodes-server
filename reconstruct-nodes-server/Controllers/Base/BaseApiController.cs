using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Filter;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;
using ReconstructNodesServer.Utility;

namespace ReconstructNodesServer.Controllers
{
	[ApiController]
	[Route("[controller]/[action]")]
    [ApiFilter]
    public class BaseApiController : BaseController
    {
		protected readonly ILogger _logger;
        protected readonly LazyServiceArgument _lazy;
        protected MailSender _mailSender => _lazy.MailSender.Value;

        protected BaseApiController(BaseApiArgument argument)
		{
            _logger = argument.Logger;
			_lazy = argument.LazyServiceArgument;
        }

        protected virtual async Task<(bool, ApiMessage?)> TransactionFunc(Func<Task> func, Func<Task>? exceptionFunc = null)
        {
            (bool isSuccess, Exception? ex) = await _lazy.BaseService.Value.TransactionFunc(func, exceptionFunc);
            if (isSuccess) { return (true, null); }
            if (ex is IApiBusinessError businessError)
            {
                _logger.LogWarning(ex, "API transaction rejected");
                return (false, businessError.ApiMessage);
            }
            _logger.LogError(ex, "API transaction failed");
            return (false, new ApiMessage(ApiMessageKeys.ErrorUnexpected));
        }

        protected IActionResult TransactionFailure(ApiMessage? message)
        {
            int status = FailureStatus(message);
            var result = BackendApiResult<object>.Create(status, message ?? new ApiMessage(ApiMessageKeys.ErrorUnexpected));
            return status == 400 ? BadRequest(result) : StatusCode(status, result);
        }

        protected static int FailureStatus(ApiMessage? message) =>
            message?.Key is "concurrency.revision_conflict" or ApiMessageKeys.EconomyRequestConflict ? 409 : 400;

        protected IActionResult SnapshotSaved<T>(IRevisionedSnapshot snapshot, T data)
        {
            if (snapshot.ExpectedRevision.HasValue && ControllerContext.HttpContext != null)
            {
                Response.Headers["X-State-Revision"] = snapshot.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return Ok(BackendApiResult<T>.Create(200, string.Empty, data));
        }

        protected async Task<IActionResult> TransactionResultAsync<T>(
            Func<Task<T>> action, string failureMessage, string? notFoundMessage = null) where T : class?
        {
            T? result = null;
            (bool success, ApiMessage? message) = await TransactionFunc(async () => result = await action());
            if (!success)
            {
                int status = FailureStatus(message);
                return StatusCode(status, BackendApiResult<string>.Create(status, message ?? failureMessage));
            }
            if (result == null)
            {
                if (notFoundMessage != null) { return StatusCode(404, BackendApiResult<string>.Create(404, notFoundMessage)); }
                throw new InvalidOperationException("交易完成但沒有回傳結果");
            }
            return Ok(BackendApiResult<T>.Create(200, string.Empty, result));
        }

        /// <summary>
        /// 依代碼於交易內執行刪除，回傳受影響筆數；代碼為空時回傳 400
        /// </summary>
        protected async Task<IActionResult> DeleteByKeyAsync(string key, string emptyMessage, string failMessage, Func<string, Task<int>> deleteFunc)
        {
            if (string.IsNullOrWhiteSpace(key)) { return StatusCode(400, BackendApiResult<int>.Create(400, emptyMessage)); }

            int rows = 0;
            (bool success, ApiMessage? message) = await TransactionFunc(async () =>
            {
                rows = await deleteFunc(key);
            });

            if (!success) { return StatusCode(400, BackendApiResult<int>.Create(400, message ?? failMessage)); }
            return Ok(BackendApiResult<int>.Create(200, string.Empty, rows));
        }

    }
}

namespace ReconstructNodesServer.Argument
{
	public class BaseApiArgument
	{
		public readonly ILogger Logger;
		public readonly LazyServiceArgument LazyServiceArgument;

        public BaseApiArgument(
            LazyServiceArgument lazyServiceArgument,
            ILoggerFactory loggerFactory)
		{
			var controllerType = GetType();
			Logger = loggerFactory.CreateLogger(controllerType);
            LazyServiceArgument = lazyServiceArgument;
        }

	}
}
