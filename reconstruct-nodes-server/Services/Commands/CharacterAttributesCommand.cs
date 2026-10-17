using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 角色能力值：整體覆寫單一角色的力量／智慧／敏捷。沿用 EquipWeapon 的「單次批次讀取 + 單次批次寫入並 COMMIT」流程；
/// 讀取該玩家全部角色與等級，在同一個 snapshot 內驗證共用點數池。每次成功都推進領域 revision，
/// 並行的另一個角色配置會對同一列寫入而失敗，因此點數池不會被兩筆交易同時超額使用。
/// </summary>
public class CharacterAttributesCommand(BaseServiceArgument argument) : BaseService(argument)
{
    private const string Operation = "character.configure_attributes";
    private const string ProgressionGroup = GameProgressParamGroups.SettingPrefix + "progression";
    private static readonly string Domain = nameof(CharacterModel);

    internal sealed class State
    {
        public List<EconomyRequest> Receipts { get; set; } = [];
        public List<PlayerParams> Revisions { get; set; } = [];
        public List<PlayerParams> Progression { get; set; } = [];
        public List<Character> Characters { get; set; } = [];
        public List<CharacterSkillTree> Trees { get; set; } = [];
        public List<CharacterSkillNode> Nodes { get; set; } = [];
        public List<Weapon> Weapons { get; set; } = [];
        public List<WeaponAffix> Affixes { get; set; } = [];
        public List<WeaponAffixValue> AffixValues { get; set; } = [];
        public List<WeaponUniqueValue> Unique { get; set; } = [];
    }

    public async Task<CommandReceipt<CharacterModel>> Execute(CharacterAttributesRequest request)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(RevisionCommand.ReceiptsRead(rows => state.Receipts = rows));
        reads.Add(RevisionCommand.RevisionsRead(rows => state.Revisions = rows));
        reads.Add(OwnedSql.Rows<PlayerParams>($"[Group] = '{ProgressionGroup}' AND Name = 'level'",
            rows => state.Progression = rows));
        // 共用點數池需要全部角色；回傳快照只需要目標角色的技能與裝備武器。
        reads.Add(OwnedSql.Rows<Character>(null, rows => state.Characters = rows));
        reads.Add(OwnedSql.ChildrenRows<CharacterSkillTree, Character>(rows => state.Trees = rows,
            activeChildren: true, condition: "p.Id = @CharacterId"));
        reads.Add(new SnapshotRead<object?>(
            OwnedSql.Descendants<CharacterSkillNode, CharacterSkillTree, Character>(
                activeChildren: true, activeMiddle: true, condition: "r.Id = @CharacterId"),
            async reader => { state.Nodes = (await reader.ReadAsync<CharacterSkillNode>()).ToList(); return null; }));
        reads.Add(new SnapshotRead<object?>($"""
            SELECT w.* INTO #EquippedWeapons FROM Weapon w
            WHERE w.CreateUserId = @CreateUserId AND w.Status != -1
                AND EXISTS (SELECT 1 FROM Character c WHERE c.CreateUserId = @CreateUserId
                    AND c.Status != -1 AND c.Id = @CharacterId AND c.WeaponEntryKey = w.EntryKey);
            SELECT * FROM #EquippedWeapons;
            SELECT c.* FROM WeaponAffix c INNER JOIN #EquippedWeapons p ON p.Id = c.WeaponId
            WHERE c.CreateUserId = @CreateUserId ORDER BY c.Sequence;
            SELECT c.* FROM WeaponAffixValue c INNER JOIN WeaponAffix p ON p.Id = c.WeaponAffixId
                INNER JOIN #EquippedWeapons w ON w.Id = p.WeaponId
            WHERE p.CreateUserId = @CreateUserId AND c.CreateUserId = @CreateUserId ORDER BY c.Sequence;
            SELECT c.* FROM WeaponUniqueValue c INNER JOIN #EquippedWeapons p ON p.Id = c.WeaponId
            WHERE c.CreateUserId = @CreateUserId ORDER BY c.Sequence;
            DROP TABLE #EquippedWeapons;
            """, async reader =>
        {
            state.Weapons = (await reader.ReadAsync<Weapon>()).ToList();
            state.Affixes = (await reader.ReadAsync<WeaponAffix>()).ToList();
            state.AffixValues = (await reader.ReadAsync<WeaponAffixValue>()).ToList();
            state.Unique = (await reader.ReadAsync<WeaponUniqueValue>()).ToList();
            return null;
        }));

        string payload = RevisionCommand.CanonicalPayload(request);
        string json = await _repository.ExecuteAtomicJsonAsync(reads.Sql, new
        {
            CreateUserId = _playerId, request.RequestId, request.CharacterId,
            RevisionNames = RevisionCommand.RevisionNames(Domain, [request.CharacterId.ToString("D")]),
        }, async reader =>
        {
            await reads.ReadAsync(reader);
            return Prepare(_playerId, request, payload, state);
        });
        return JsonSerializer.Deserialize<CommandReceipt<CharacterModel>>(json)
            ?? throw new InvalidOperationException("Invalid command receipt.");
    }

    internal static IReadOnlyList<SqlBatchCommand> Prepare(Guid? playerId, CharacterAttributesRequest request,
        string payload, State state)
    {
        if (RevisionCommand.Replay(state.Receipts.FirstOrDefault(), Operation, payload) is { } replay) { return replay; }

        Character character = state.Characters.FirstOrDefault(row => row.Id == request.CharacterId)
            ?? throw new BusinessArgumentException(ApiMessageKeys.CharacterNotOwned);
        int level = state.Progression.Select(row => JsonSerializer.Deserialize<int>(row.Value))
            .DefaultIfEmpty(CharacterAttributeRules.StartLevel).First();
        int allocatedByOthers = state.Characters.Where(row => row.Id != character.Id)
            .Sum(row => row.Strength + row.Intelligence + row.Dexterity);
        CharacterAttributeRules.Validate(request, level, allocatedByOthers);

        var writes = new SqlWriteBatch(playerId);
        writes.Add($"""
            UPDATE {OwnedSql.Table<Character>()} SET Strength = @Strength, Intelligence = @Intelligence, Dexterity = @Dexterity
            WHERE {OwnedSql.Where("Id = @Id")};
            """, new { Id = character.Id, request.Strength, request.Intelligence, request.Dexterity });
        character.Strength = request.Strength;
        character.Intelligence = request.Intelligence;
        character.Dexterity = request.Dexterity;

        long next = RevisionCommand.AdvanceRows(writes, state.Revisions, Domain, [request.CharacterId.ToString("D")]);
        var weapons = WeaponService.BuildSnapshots(state.Weapons, state.Affixes, state.AffixValues, state.Unique, 0)
            .ToDictionary(weapon => weapon.Id);
        CharacterModel snapshot = CharacterService.BuildSnapshots([character], state.Trees, state.Nodes, weapons, next).Single();
        RevisionCommand.CompleteReceipt(writes, request, Operation, payload, next, snapshot);
        return writes.Commands;
    }
}
