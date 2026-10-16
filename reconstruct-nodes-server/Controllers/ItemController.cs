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
        /// 依物品代碼軟刪除本人物品
        /// </summary>
        [HttpPost]
        public virtual Task<IActionResult> Delete(ItemKeyRequest request)
            => DeleteByKeyAsync(request.ItemKey, ApiMessageKeys.ItemKeyRequired, ApiMessageKeys.ItemDeleteFailed, service.DeleteByNaturalKey);
    }
}
