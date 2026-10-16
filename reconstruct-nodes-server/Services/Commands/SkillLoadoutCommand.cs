using System.Globalization;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 角色技能配置：沿用 WaveComplete 的「單次批次讀取 + 單次批次寫入並 COMMIT」流程，
/// 驗證後只替換該角色的技能樹，revision fence 細分到角色實例（原本約 30 次 SQL 往返）。
/// </summary>
public class SkillLoadoutCommand(BaseServiceArgument argument) : BaseService(argument)
{
    private const string Operation = "foreground.skill_loadout";
    private static readonly string Domain = nameof(CharacterModel);

    private sealed class State
    {
        public List<EconomyRequest> Receipts { get; set; } = [];
        public List<PlayerParams> Revisions { get; set; } = [];
        public List<PlayerParams> Unlocks { get; set; } = [];
        public List<Character> Characters { get; set; } = [];
        public List<Weapon> Weapons { get; set; } = [];
        public List<WeaponAffix> Affixes { get; set; } = [];
        public List<WeaponAffixValue> AffixValues { get; set; } = [];
        public List<WeaponUniqueValue> Unique { get; set; } = [];
    }

    public async Task<CommandReceipt<CharacterModel>> Execute(SkillLoadoutRequest request)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        var state = new State();
        string scopeKey = RevisionCommand.ScopeKey(Domain, request.CharacterId.ToString("D"));
        var reads = new SnapshotReadBatch();
        reads.Add(OwnedSql.Rows<EconomyRequest>("RequestId = @RequestId", rows => state.Receipts = rows, activeOnly: false));
        reads.Add(OwnedSql.Rows<PlayerParams>(
            "[Group] = @RevisionGroup AND Name IN (@Domain, @ScopeKey)", rows => state.Revisions = rows));
        reads.Add(OwnedSql.Rows<PlayerParams>(CharacterService.SkillAccessCondition, rows => state.Unlocks = rows));
        reads.Add(OwnedSql.Rows<Character>("Id = @CharacterId", rows => state.Characters = rows));
        // 只讀這個角色裝備的武器，供回傳快照使用。
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

        string payload = JsonSerializer.Serialize(request);
        string json = await _repository.ExecuteAtomicJsonAsync(reads.Sql, new
        {
            CreateUserId = _playerId, request.RequestId, request.CharacterId,
            RevisionGroup = RevisionCommand.RevisionGroup, Domain, ScopeKey = scopeKey,
        }, async reader =>
        {
            await reads.ReadAsync(reader);
            return Prepare(request, payload, scopeKey, state);
        });
        return JsonSerializer.Deserialize<CommandReceipt<CharacterModel>>(json)
            ?? throw new InvalidOperationException("Invalid command receipt.");
    }

    private IReadOnlyList<SqlBatchCommand> Prepare(SkillLoadoutRequest request, string payload, string scopeKey, State state)
    {
        var prior = state.Receipts.FirstOrDefault();
        if (prior != null)
        {
            if (prior.Operation != Operation || prior.Payload != payload)
            { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
            return [new SqlBatchCommand("SELECT @Receipt;", new { Receipt = prior.Result })];
        }

        PlayerParams? domainRow = state.Revisions.FirstOrDefault(r => r.Name == Domain);
        PlayerParams? scopeRow = state.Revisions.FirstOrDefault(r => r.Name == scopeKey);
        long current = domainRow == null ? 0 : long.Parse(domainRow.Value, CultureInfo.InvariantCulture);
        long fence = current > 0 && scopeRow != null ? long.Parse(scopeRow.Value, CultureInfo.InvariantCulture) : current > 0 ? 0 : current;
        RevisionCommand.CheckValues(Domain, request.ExpectedRevision, current, fence);

        SkillLoadoutRules.Validate(request.SkillTrees);
        Character character = state.Characters.SingleOrDefault(c => c.Status != Library.Models.Enum.EStatus.刪除)
            ?? throw new BusinessArgumentException(ApiMessageKeys.CharacterNotOwned);
        CharacterService.ValidateSkillAccess(character.Kind, request.SkillTrees, state.Unlocks);

        List<CharacterSkillTree> trees = [];
        List<CharacterSkillNode> nodes = [];
        foreach (GameProgressSkillLoadoutModel input in request.SkillTrees)
        {
            CharacterSkillTree tree = new() { CharacterId = character.Id, Kind = input.Kind, Sequence = trees.Count };
            trees.Add(tree);
            int sequence = 0;
            foreach (GameProgressSkillNodeModel node in input.AuxiliaryNodes ?? [])
            {
                nodes.Add(new CharacterSkillNode
                {
                    CharacterSkillTreeId = tree.Id, NodeType = node.Type,
                    LootId = string.IsNullOrEmpty(node.LootId) ? null : node.LootId,
                    Kind = node.Kind, Sequence = sequence++,
                });
            }
        }

        long next = checked(current + 1);
        var weapons = WeaponService.BuildSnapshots(state.Weapons, state.Affixes, state.AffixValues, state.Unique, 0)
            .ToDictionary(weapon => weapon.Id);
        CommandReceipt<CharacterModel> receipt = new()
        {
            RequestId = request.RequestId, Revision = next,
            Snapshot = CharacterService.BuildSnapshots([character], trees, nodes, weapons, next).Single(),
        };
        string resultJson = JsonSerializer.Serialize(receipt);

        var writes = new SqlWriteBatch(_playerId);
        writes.Add($"""
            {OwnedSql.Delete<CharacterSkillNode>("CharacterSkillTreeId IN (SELECT Id FROM CharacterSkillTree WHERE CharacterId = @CharacterId AND CreateUserId = @CreateUserId)")}
            {OwnedSql.Delete<CharacterSkillTree>("CharacterId = @CharacterId")}
            """, new { request.CharacterId });
        writes.Insert(trees);
        writes.Insert(nodes);
        List<PlayerParams> newRows = [], updatedRows = [];
        void Advance(PlayerParams? row, string name)
        {
            if (row == null)
            { newRows.Add(new PlayerParams { Group = RevisionCommand.RevisionGroup, Name = name, Value = next.ToString(CultureInfo.InvariantCulture) }); }
            else { row.Value = next.ToString(CultureInfo.InvariantCulture); updatedRows.Add(row); }
        }
        Advance(domainRow, Domain);
        Advance(scopeRow, scopeKey);
        writes.Insert(newRows);
        writes.Update(updatedRows);
        writes.Insert([new EconomyRequest { RequestId = request.RequestId, Operation = Operation,
            Payload = payload, Result = resultJson }]);
        writes.Add("SELECT @Result;", new { Result = resultJson });
        return writes.Commands;
    }
}
