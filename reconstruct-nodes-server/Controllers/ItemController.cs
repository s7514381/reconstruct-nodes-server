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
                return StatusCode(400, BackendApiResult<int>.Create(400, "物品代碼不可為空，且增量不可為 0"));
            }

            int amount = 0;
            (bool success, string? message) = await TransactionFunc(async () =>
            {
                amount = await service.AdjustAmount(request.ItemKey, request.Delta, request.Name, request.Category);
            });

            if (!success) { return StatusCode(400, BackendApiResult<int>.Create(400, message ?? "物品數量更新失敗")); }
            return Ok(BackendApiResult<int>.Create(200, string.Empty, amount));
        }

        /// <summary>
        /// 依物品代碼軟刪除本人物品
        /// </summary>
        [HttpPost]
        public virtual Task<IActionResult> Delete(ItemKeyRequest request)
            => DeleteByKeyAsync(request.ItemKey, "物品代碼不可為空", "物品刪除失敗", service.DeleteByNaturalKey);
    }
}
