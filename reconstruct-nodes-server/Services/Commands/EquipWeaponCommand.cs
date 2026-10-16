using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 角色穿卸武器：沿用 WaveComplete 的「單次批次讀取 + 單次批次寫入並 COMMIT」流程，固定 2 次 SQL 往返。
/// 只更新角色的武器引用，並碰觸武器列，讓同時分解同一把武器的交易以寫入衝突收場。
/// </summary>
public class EquipWeaponCommand(BaseServiceArgument argument) : BaseService(argument)
{
    private const string Operation = "character.equip_weapon";
    private const int WeaponNotOwnedError = 51009;
    private static readonly string Domain = nameof(CharacterModel);

    internal sealed class State
    {
        public List<EconomyRequest> Receipts { get; set; } = [];
        public List<PlayerParams> Revisions { get; set; } = [];
        public List<Character> Characters { get; set; } = [];
        public List<CharacterSkillTree> Trees { get; set; } = [];
        public List<CharacterSkillNode> Nodes { get; set; } = [];
        public List<Weapon> Weapons { get; set; } = [];
        public List<WeaponAffix> Affixes { get; set; } = [];
        public List<WeaponAffixValue> AffixValues { get; set; } = [];
        public List<WeaponUniqueValue> Unique { get; set; } = [];
    }

    public async Task<CommandReceipt<CharacterModel>> Execute(WeaponEquipRequest request)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(OwnedSql.OwnerRead("Character"));
        reads.Add(RevisionCommand.ReceiptsRead(rows => state.Receipts = rows));
        reads.Add(RevisionCommand.RevisionsRead(rows => state.Revisions = rows));
        // 目標角色，以及（以資料庫定序比對）已裝備這把武器的角色。
        reads.Add(OwnedSql.Rows<Character>("Id = @CharacterId OR WeaponEntryKey = @WeaponEntryKey",
            rows => state.Characters = rows));
        reads.Add(OwnedSql.ChildrenRows<CharacterSkillTree, Character>(rows => state.Trees = rows,
            activeChildren: true, condition: "p.Id = @CharacterId"));
        reads.Add(new SnapshotRead<object?>(
            OwnedSql.Descendants<CharacterSkillNode, CharacterSkillTree, Character>(
                activeChildren: true, activeMiddle: true, condition: "r.Id = @CharacterId"),
            async reader => { state.Nodes = (await reader.ReadAsync<CharacterSkillNode>()).ToList(); return null; }));
        reads.Add(OwnedSql.Rows<Weapon>("EntryKey = @WeaponEntryKey", rows => state.Weapons = rows));
        reads.Add(OwnedSql.ChildrenRows<WeaponAffix, Weapon>(rows => state.Affixes = rows,
            condition: "p.EntryKey = @WeaponEntryKey"));
        reads.Add(new SnapshotRead<object?>(
            OwnedSql.Descendants<WeaponAffixValue, WeaponAffix, Weapon>(condition: "r.EntryKey = @WeaponEntryKey"),
            async reader => { state.AffixValues = (await reader.ReadAsync<WeaponAffixValue>()).ToList(); return null; }));
        reads.Add(OwnedSql.ChildrenRows<WeaponUniqueValue, Weapon>(rows => state.Unique = rows,
            condition: "p.EntryKey = @WeaponEntryKey"));

        string payload = RevisionCommand.CanonicalPayload(request);
        try
        {
            string json = await _repository.ExecuteAtomicJsonAsync(reads.Sql, new
            {
                CreateUserId = _playerId, request.RequestId, request.CharacterId,
                WeaponEntryKey = string.IsNullOrEmpty(request.WeaponEntryId) ? null : request.WeaponEntryId,
                RevisionNames = RevisionCommand.RevisionNames(Domain, [request.CharacterId.ToString("D")]),
            }, async reader =>
            {
                await reads.ReadAsync(reader);
                return Prepare(_playerId, request, payload, state);
            });
            return JsonSerializer.Deserialize<CommandReceipt<CharacterModel>>(json)
                ?? throw new InvalidOperationException("Invalid command receipt.");
        }
        catch (System.Data.SqlClient.SqlException error) when (error.Number == WeaponNotOwnedError)
        {
            throw new BusinessArgumentException(ApiMessageKeys.WeaponNotOwned);
        }
    }

    internal static IReadOnlyList<SqlBatchCommand> Prepare(Guid? playerId, WeaponEquipRequest request,
        string payload, State state)
    {
        if (RevisionCommand.Replay(state.Receipts.FirstOrDefault(), Operation, payload) is { } replay) { return replay; }

        Character character = state.Characters.FirstOrDefault(row => row.Id == request.CharacterId)
            ?? throw new BusinessArgumentException(ApiMessageKeys.CharacterNotOwned);
        string key = request.WeaponEntryId ?? "";
        CharacterService.ValidateWeaponKey(key);
        string? weaponKey = key.Length == 0 ? null : key;
        if (weaponKey != null)
        {
            if (state.Weapons.Count == 0) { throw new BusinessArgumentException(ApiMessageKeys.WeaponNotOwned); }
            if (state.Characters.Any(row => row.Id != character.Id))
            { throw new BusinessArgumentException(ApiMessageKeys.WeaponAlreadyEquipped); }
            if (state.Weapons.Count != 1) { throw new BusinessArgumentException(ApiMessageKeys.WeaponNotOwned); }
        }

        var writes = new SqlWriteBatch(playerId);
        if (weaponKey != null)
        {
            writes.Add($"""
                UPDATE {OwnedSql.Table<Weapon>()} SET Rarity = Rarity
                WHERE {OwnedSql.Where("EntryKey = @EntryKey")};
                IF @@ROWCOUNT <> 1 THROW {WeaponNotOwnedError}, 'Weapon is not owned', 1;
                """, new { EntryKey = weaponKey });
        }
        int? quality = weaponKey == null ? null : 2;
        writes.Add($"""
            UPDATE {OwnedSql.Table<Character>()} SET WeaponEntryKey = @WeaponEntryKey, WeaponQualityVersion = @Quality
            WHERE {OwnedSql.Where("Id = @Id")};
            """, new { Id = character.Id, WeaponEntryKey = weaponKey, Quality = quality });
        character.WeaponEntryKey = weaponKey!;
        character.WeaponQualityVersion = quality;

        long next = RevisionCommand.AdvanceRows(writes, state.Revisions, Domain, [request.CharacterId.ToString("D")]);
        var weapons = WeaponService.BuildSnapshots(state.Weapons, state.Affixes, state.AffixValues, state.Unique, 0)
            .ToDictionary(weapon => weapon.Id);
        CharacterModel snapshot = CharacterService.BuildSnapshots([character], state.Trees, state.Nodes, weapons, next).Single();
        RevisionCommand.CompleteReceipt(writes, request, Operation, payload, next, snapshot);
        return writes.Commands;
    }
}
