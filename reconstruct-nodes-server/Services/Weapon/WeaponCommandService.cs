using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services;

public class WeaponCommandService(BaseServiceArgument argument, RevisionService revisions,
    WeaponService weapons, ItemService items, CharacterService characters) : BaseService(argument)
{
    public Task<CommandReceipt<WeaponInstanceModel>> Acquire(WeaponAcquireRequest request) =>
        revisions.ExecuteIntent(request, "weapon.acquire", nameof(WeaponInstanceModel), request, async () =>
        {
            if (request.Weapon == null) { throw new BusinessArgumentException(ApiMessageKeys.RequestInvalid); }
            await weapons.SaveForCommand(request.Weapon, acquire: true);
            return (await weapons.GetInstanceList()).Single(w => w.Id == request.Weapon.Id);
        });

    public Task<CommandReceipt<WeaponRefineSnapshot>> Refine(WeaponRefineRequest request) =>
        revisions.ExecuteIntent(request, "weapon.refine", nameof(WeaponInstanceModel), request, async () =>
        {
            WeaponInstanceModel original = (await weapons.GetInstanceList()).SingleOrDefault(w => w.Id == request.WeaponEntryId)
                ?? throw new BusinessArgumentException(ApiMessageKeys.WeaponNotOwned);
            WeaponInstanceModel candidate = RefinementRules.Refine(original, request.CrystalId);
            // Both the generated result and the debit are protected by the enclosing SQL transaction.
            int amount = await items.AdjustAmount(request.CrystalId, -1);
            await weapons.SaveForCommand(candidate);
            return new WeaponRefineSnapshot
            {
                Weapon = (await weapons.GetInstanceList()).Single(w => w.Id == request.WeaponEntryId),
                CrystalId = request.CrystalId, MaterialCount = amount,
            };
        });

    public Task<CommandReceipt<WeaponDisassembleSnapshot>> Disassemble(WeaponDisassembleRequest request)
    {
        if (request.WeaponEntryIds == null || request.WeaponEntryIds.Count > 500 ||
            request.WeaponEntryIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 450))
        { throw new BusinessArgumentException(ApiMessageKeys.RequestInvalid); }
        // A batch is a set: reordered or duplicate IDs are still the same intent.
        request = new WeaponDisassembleRequest
        {
            RequestId = request.RequestId,
            WeaponEntryIds = request.WeaponEntryIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
        };
        return revisions.ExecuteIntent(request, "weapon.disassemble", nameof(WeaponInstanceModel), request, async () =>
        {
            var owned = (await weapons.GetInstanceList()).Select(w => w.Id).ToHashSet();
            var equipped = (await characters.GetList()).Select(c => c.WeaponEntryId).ToHashSet();
            WeaponDisassembleSnapshot result = new();
            foreach (string id in request.WeaponEntryIds.Where(id => !string.IsNullOrEmpty(id)).Distinct())
            {
                if (!owned.Contains(id) || equipped.Contains(id)) { result.SkippedIds.Add(id); continue; }
                await weapons.DeleteForCommand(id);
                result.RemovedIds.Add(id);
            }
            // Client semantics: skip protected/missing entries, with no material or currency reward.
            await revisions.Advance(nameof(WeaponInstanceModel), result.RemovedIds);
            result.Weapons = (await weapons.GetInstanceList()).ToList();
            return result;
        });
    }
}
