using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 角色技能連線數升級：同一交易內扣除能量萃取器並把該角色連線數加一。
/// 沿用「單次批次讀取 + 單次批次寫入並 COMMIT」流程；費用只由 <see cref="SkillLinkRules"/> 決定，
/// 數量不足由物品調整 SQL 擲出 51001，與其他角色／武器命令一樣以角色 revision fence 擋並行。
/// </summary>
public class SkillLinkUpgradeCommand(BaseServiceArgument argument) : BaseService(argument)
{
    private const string Operation = "character.upgrade_skill_links";
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

    public async Task<CommandReceipt<SkillLinkUpgradeSnapshot>> Execute(SkillLinkUpgradeRequest request)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(RevisionCommand.ReceiptsRead(rows => state.Receipts = rows));
        reads.Add(RevisionCommand.RevisionsRead(rows => state.Revisions = rows));
        reads.Add(OwnedSql.Rows<Character>("Id = @CharacterId", rows => state.Characters = rows));
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
        try
        {
            string json = await _repository.ExecuteAtomicJsonAsync(reads.Sql, new
            {
                CreateUserId = _playerId, request.RequestId, request.CharacterId,
                RevisionNames = RevisionCommand.RevisionNames(Domain, [request.CharacterId.ToString("D")]),
            }, async reader =>
            {
                await reads.ReadAsync(reader);
                return Prepare(_playerId, request, payload, state);
            });
            return JsonSerializer.Deserialize<CommandReceipt<SkillLinkUpgradeSnapshot>>(json)
                ?? throw new InvalidOperationException("Invalid command receipt.");
        }
        catch (System.Data.SqlClient.SqlException error) when (error.Number == 51001)
        { throw new BusinessOperationException(ApiMessageKeys.ItemInsufficient); }
    }

    internal static IReadOnlyList<SqlBatchCommand> Prepare(Guid? playerId, SkillLinkUpgradeRequest request,
        string payload, State state)
    {
        if (RevisionCommand.Replay(state.Receipts.FirstOrDefault(), Operation, payload) is { } replay) { return replay; }

        Character character = state.Characters.FirstOrDefault(row => row.Id == request.CharacterId)
            ?? throw new BusinessArgumentException(ApiMessageKeys.CharacterNotOwned);
        int cost = SkillLinkRules.UpgradeCost(character.SkillLinkCount)
            ?? throw new BusinessArgumentException(ApiMessageKeys.CharacterSkillLinkMax);

        var writes = new SqlWriteBatch(playerId);
        writes.Add($"""
            UPDATE {OwnedSql.Table<Character>()} SET SkillLinkCount = @SkillLinkCount
            WHERE {OwnedSql.Where("Id = @Id")};
            """, new { character.Id, SkillLinkCount = character.SkillLinkCount + 1 });
        character.SkillLinkCount += 1;

        long next = RevisionCommand.AdvanceRows(writes, state.Revisions, Domain, [request.CharacterId.ToString("D")]);
        var weapons = WeaponService.BuildSnapshots(state.Weapons, state.Affixes, state.AffixValues, state.Unique, 0)
            .ToDictionary(weapon => weapon.Id);
        var receipt = new CommandReceipt<SkillLinkUpgradeSnapshot>
        {
            RequestId = request.RequestId, Revision = next,
            Snapshot = new()
            {
                Character = CharacterService.BuildSnapshots([character], state.Trees, state.Nodes, weapons, next).Single(),
            },
        };
        var now = DateTime.Now;
        writes.Add(ItemService.AdjustmentSql + """
            DECLARE @Result nvarchar(max) = JSON_MODIFY(@Receipt, '$.snapshot.material_count', @Amount);
            INSERT INTO EconomyRequest (Id, CreateDate, CreateUserId, Status, RequestId, Operation, Payload, Result)
            VALUES (NEWID(), @CreateDate, @CreateUserId, @Status, @RequestId, @Operation, @Payload, @Result);
            SELECT @Result;
            """, new
        {
            ItemKey = SkillLinkRules.UpgradeItemKey,
            Delta = -cost,
            Category = (string?)null,
            NewId = Guid.NewGuid(),
            CreateDate = now,
            Status = (int)Library.Models.Enum.EStatus.啟用,
            Whitespace = ItemService.WhitespaceCharacters,
            Receipt = JsonSerializer.Serialize(receipt),
            request.RequestId,
            Operation,
            Payload = payload,
        });
        return writes.Commands;
    }
}
