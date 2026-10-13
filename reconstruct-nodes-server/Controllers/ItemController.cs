using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class ItemController(BaseApiArgument argument, ItemService service) : AuthorizeApiController(argument)
    {
        [HttpPost]
        public virtual async Task<IActionResult> GetInStockList()
        {
            IEnumerable<Item> list = await service.GetInStockList();
            return Ok(BackendApiResult<IEnumerable<Item>>.Create(200, string.Empty, list));
        }

        /// <summary>
        /// 以增量調整物品數量（Delta 正數取得、負數消耗），回傳調整後數量
        /// </summary>
        [HttpPost]
        public virtual async Task<IActionResult> AdjustAmount(ItemDeltaRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ItemKey) || request.Delta == 0)
            {
                return StatusCode(400, BackendApiResult<int>.Create(400, ApiMessageKeys.ItemDeltaInvalid));
            }

            int amount = 0;
            (bool success, ApiMessage? message) = await TransactionFunc(async () =>
            {
                amount = await service.AdjustAmount(request.ItemKey, request.Delta, request.Category);
            });

            if (!success) { return StatusCode(400, BackendApiResult<int>.Create(400, message ?? ApiMessageKeys.ItemUpdateFailed)); }
            return Ok(BackendApiResult<int>.Create(200, string.Empty, amount));
        }

        /// <summary>
        /// 依物品代碼軟刪除本人物品
        /// </summary>
        [HttpPost]
        public virtual Task<IActionResult> Delete(ItemKeyRequest request)
            => DeleteByKeyAsync(request.ItemKey, ApiMessageKeys.ItemKeyRequired, ApiMessageKeys.ItemDeleteFailed, service.DeleteByNaturalKey);
    }
}
