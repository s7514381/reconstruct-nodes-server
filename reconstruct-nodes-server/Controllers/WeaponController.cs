using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class WeaponController(BaseApiArgument argument, WeaponService service) : AuthorizeApiController(argument)
    {
        [HttpPost]
        public virtual async Task<IActionResult> GetInstanceList()
        {
            IEnumerable<WeaponInstanceModel> list = await service.GetInstanceList();
            return Ok(BackendApiResult<IEnumerable<WeaponInstanceModel>>.Create(200, string.Empty, list));
        }

        /// <summary>
        /// 儲存單把背包武器（以 id 為實例代碼做 upsert）
        /// </summary>
        [HttpPost]
        public virtual async Task<IActionResult> Save(WeaponInstanceModel model)
        {
            (bool success, ApiMessage? message) = await TransactionFunc(async () =>
            {
                await service.Save(model);
            });

            if (!success) { return StatusCode(400, BackendApiResult<string>.Create(400, message ?? ApiMessageKeys.WeaponSaveFailed)); }
            return Ok(BackendApiResult<string>.Create(200, string.Empty, model.Id));
        }

        /// <summary>
        /// 軟刪除武器；仍被角色裝備參照時回傳 400
        /// </summary>
        [HttpPost]
        public virtual Task<IActionResult> Delete(EntryKeyRequest request)
            => DeleteByKeyAsync(request.EntryKey, ApiMessageKeys.WeaponIdRequired, ApiMessageKeys.WeaponDeleteFailed, service.DeleteByNaturalKey);
    }
}
