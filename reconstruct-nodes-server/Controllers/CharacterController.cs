using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class CharacterController(BaseApiArgument argument, CharacterService service) : AuthorizeApiController(argument)
    {
        [HttpPost]
        public Task<IActionResult> EquipWeapon(WeaponEquipRequest request) =>
            TransactionResultAsync(() => service.EquipWeapon(request), ApiMessageKeys.CharacterSaveFailed);

        [HttpPost]
        public virtual Task<IActionResult> GetList() =>
            TransactionResultAsync<List<CharacterModel>>(() => service.GetListCommand(),
                ApiMessageKeys.CharacterReadFailed, commandOwnsTransaction: true);

        [HttpPost]
        public virtual Task<IActionResult> Get(Guid id) =>
            TransactionResultAsync<CharacterModel?>(() => service.Get(id), ApiMessageKeys.CharacterReadFailed, ApiMessageKeys.CharacterMissing);

        [HttpPost]
        public virtual async Task<IActionResult> Save(CharacterModel model)
        {
            (bool success, ApiMessage? message) = await TransactionFunc(() => service.Save(model));
            if (!success) { return TransactionFailure(message ?? new ApiMessage(ApiMessageKeys.CharacterSaveFailed)); }
            return SnapshotSaved(model, model.Id);
        }
    }
}
