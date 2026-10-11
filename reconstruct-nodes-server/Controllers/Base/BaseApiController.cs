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

        protected virtual async Task<(bool, string?)> TransactionFunc(Func<Task> func, Func<Task>? exceptionFunc = null)
        {
            (bool isSuccess, Exception? ex) = await _lazy.BaseService.Value.TransactionFunc(func, exceptionFunc);
            return (isSuccess, ex?.Message);
        }

        /// <summary>
        /// 依代碼於交易內執行刪除，回傳受影響筆數；代碼為空時回傳 400
        /// </summary>
        protected async Task<IActionResult> DeleteByKeyAsync(string key, string emptyMessage, string failMessage, Func<string, Task<int>> deleteFunc)
        {
            if (string.IsNullOrWhiteSpace(key)) { return StatusCode(400, BackendApiResult<int>.Create(400, emptyMessage)); }

            int rows = 0;
            (bool success, string? message) = await TransactionFunc(async () =>
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

