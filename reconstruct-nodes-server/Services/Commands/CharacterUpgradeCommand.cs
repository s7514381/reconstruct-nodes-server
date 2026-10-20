using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 升級角色：同一交易內移除任意數量（至少 2^(Level-1) 隻）素材角色（歸還武器與能力點、刪除相關資料，不給榮譽），並依素材數量換算讓目標連升數級（見 LevelAfterMaterials）。
/// 沿用「單次批次讀取 + 單次批次寫入並 COMMIT」流程與 EconomyRequest 回執：同一 request_id 重送回原收據。
/// 核心不可當目標或素材；場上（有部署紀錄）的素材必須先撤下。
/// </summary>
public class CharacterUpgradeCommand(BaseServiceArgument argument) : BaseService(argument)
{
    private const string Operation = "character.upgrade";
    private static readonly string Domain = nameof(CharacterModel);

    internal sealed class State
    {
        public List<EconomyRequest> Receipts { get; set; } = [];
        public List<PlayerParams> Revisions { get; set; } = [];
        public List<Character> Characters { get; set; } = [];
        public List<Deployment> Deployments { get; set; } = [];
    }

    public async Task<CommandReceipt<CharacterUpgradeSnapshot>> Execute(CharacterUpgradeRequest request)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        request.MaterialIds = request.MaterialIds.Order().ToList();
        Guid[] ids = [request.TargetId, .. request.MaterialIds];
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(RevisionCommand.ReceiptsRead(rows => state.Receipts = rows));
        reads.Add(RevisionCommand.RevisionsRead(rows => state.Revisions = rows));
        reads.Add(OwnedSql.Rows<Character>("Id IN @CharacterIds", rows => state.Characters = rows));
        reads.Add(OwnedSql.Rows<Deployment>("CharacterId IN @CharacterIds", rows => state.Deployments = rows));

        string payload = RevisionCommand.CanonicalPayload(request);
        string json = await _repository.ExecuteAtomicJsonAsync(reads.Sql, new
        {
            CreateUserId = _playerId, request.RequestId, CharacterIds = ids,
            RevisionNames = RevisionCommand.RevisionNames(Domain, ids.Select(id => id.ToString("D"))),
        }, async reader =>
        {
            await reads.ReadAsync(reader);
            return Prepare(_playerId, request, payload, state);
        });
        return JsonSerializer.Deserialize<CommandReceipt<CharacterUpgradeSnapshot>>(json)
            ?? throw new InvalidOperationException("Invalid command receipt.");
    }

    internal static IReadOnlyList<SqlBatchCommand> Prepare(Guid? playerId, CharacterUpgradeRequest request,
        string payload, State state)
    {
        if (RevisionCommand.Replay(state.Receipts.FirstOrDefault(), Operation, payload) is { } replay) { return replay; }

        Character target = state.Characters.FirstOrDefault(row => row.Id == request.TargetId)
            ?? throw new BusinessArgumentException(ApiMessageKeys.CharacterNotOwned);
        if (target.Kind == CharacterKind.Core) { throw new BusinessArgumentException(ApiMessageKeys.CharacterUpgradeMaterialInvalid); }
        int needed = CharacterLevelRules.UpgradeMaterialCount(target.Level);
        if (needed == 0) { throw new BusinessArgumentException(ApiMessageKeys.CharacterUpgradeMax); }

        // 數量、重複、含目標或核心都屬於素材不合法；不在本人名下的角色以 CharacterNotOwned 回報。
        Guid[] materialIds = request.MaterialIds.ToArray();
        if (materialIds.Length < needed || materialIds.Distinct().Count() != materialIds.Length
            || materialIds.Contains(target.Id))
        { throw new BusinessArgumentException(ApiMessageKeys.CharacterUpgradeMaterialInvalid); }
        List<Character> materials = [];
        foreach (Guid id in materialIds)
        {
            Character material = state.Characters.FirstOrDefault(row => row.Id == id)
                ?? throw new BusinessArgumentException(ApiMessageKeys.CharacterNotOwned);
            if (material.Kind == CharacterKind.Core) { throw new BusinessArgumentException(ApiMessageKeys.CharacterUpgradeMaterialInvalid); }
            if (state.Deployments.Any(row => row.CharacterId == id))
            { throw new BusinessArgumentException(ApiMessageKeys.CharacterDissolveDeployed); }
            materials.Add(material);
        }

        int newLevel = CharacterLevelRules.LevelAfterMaterials(target.Level, materialIds.Length);
        var writes = new SqlWriteBatch(playerId);
        CharacterRemoval.AddWrites(writes, materials);
        writes.Add($"UPDATE {OwnedSql.Table<Character>()} SET Level = @Level WHERE {OwnedSql.Where("Id = @TargetId")};",
            new { Level = newLevel, TargetId = target.Id });
        long next = RevisionCommand.AdvanceRows(writes, state.Revisions, Domain,
            new[] { target.Id }.Concat(materialIds).Select(id => id.ToString("D")));
        RevisionCommand.CompleteReceipt(writes, request, Operation, payload, next, new CharacterUpgradeSnapshot
        {
            TargetId = target.Id,
            Level = newLevel,
            RemovedIds = materialIds.ToList(),
            ReleasedWeaponEntryIds = materials.Select(row => row.WeaponEntryKey)
                .Where(key => !string.IsNullOrEmpty(key)).Cast<string>().ToList(),
        });
        return writes.Commands;
    }
}
