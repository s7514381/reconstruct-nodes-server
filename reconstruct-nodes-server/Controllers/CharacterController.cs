using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class CharacterController(BaseApiArgument argument, CharacterService service, SkillLoadoutCommand skills,
        EquipWeaponCommand equip) : AuthorizeApiController(argument)
    {
        [HttpPost]
        public Task<IActionResult> EquipWeapon(WeaponEquipRequest request) =>
            TransactionResultAsync(() => equip.Execute(request), ApiMessageKeys.CharacterSaveFailed,
                commandOwnsTransaction: true);

        [HttpPost]
        public Task<IActionResult> ConfigureSkills(SkillLoadoutRequest request) =>
            TransactionResultAsync(() => skills.Execute(request), ApiMessageKeys.ErrorUnexpected,
                commandOwnsTransaction: true);

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
