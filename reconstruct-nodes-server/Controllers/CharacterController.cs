using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
    public class CharacterController(BaseApiArgument argument, CharacterService service, SkillLoadoutCommand skills,
        EquipWeaponCommand equip, CharacterAttributesCommand attributes, SkillLinkUpgradeCommand skillLinks,
        CharacterDissolveCommand dissolve, CharacterUpgradeCommand upgrade)
        : AuthorizeApiController(argument)
    {
        [HttpPost]
        public Task<IActionResult> Upgrade(CharacterUpgradeRequest request) =>
            TransactionResultAsync(() => upgrade.Execute(request), ApiMessageKeys.CharacterSaveFailed,
                commandOwnsTransaction: true);

        [HttpPost]
        public Task<IActionResult> Dissolve(CharacterDissolveRequest request) =>
            TransactionResultAsync(() => dissolve.Execute(request), ApiMessageKeys.CharacterSaveFailed,
                commandOwnsTransaction: true);

        [HttpPost]
        public Task<IActionResult> UpgradeSkillLinks(SkillLinkUpgradeRequest request) =>
            TransactionResultAsync(() => skillLinks.Execute(request), ApiMessageKeys.CharacterSaveFailed,
                commandOwnsTransaction: true);

        [HttpPost]
        public Task<IActionResult> ConfigureAttributes(CharacterAttributesRequest request) =>
            TransactionResultAsync(() => attributes.Execute(request), ApiMessageKeys.CharacterSaveFailed,
                commandOwnsTransaction: true);

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
