using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 解散角色：同一交易內解除裝備、軟刪除角色與其技能資料，並依等級發放榮譽。
/// 沿用「單次批次讀取 + 單次批次寫入並 COMMIT」流程與 EconomyRequest 回執：同一 request_id 重送回原收據，
/// 不會重複發放榮譽；核心不可解散，場上（有部署紀錄）的角色必須先撤下，避免繞過戰場配置的 revision。
/// </summary>
public class CharacterDissolveCommand(BaseServiceArgument argument) : BaseService(argument)
{
    private const string Operation = "character.dissolve";
    private static readonly string Domain = nameof(CharacterModel);

    internal sealed class State
    {
        public List<EconomyRequest> Receipts { get; set; } = [];
        public List<PlayerParams> Revisions { get; set; } = [];
        public List<Character> Characters { get; set; } = [];
        public List<Deployment> Deployments { get; set; } = [];
    }

    public async Task<CommandReceipt<CharacterDissolveSnapshot>> Execute(CharacterDissolveRequest request)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(RevisionCommand.ReceiptsRead(rows => state.Receipts = rows));
        reads.Add(RevisionCommand.RevisionsRead(rows => state.Revisions = rows));
        reads.Add(OwnedSql.Rows<Character>("Id = @CharacterId", rows => state.Characters = rows));
        reads.Add(OwnedSql.Rows<Deployment>("CharacterId = @CharacterId", rows => state.Deployments = rows));

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
        return JsonSerializer.Deserialize<CommandReceipt<CharacterDissolveSnapshot>>(json)
            ?? throw new InvalidOperationException("Invalid command receipt.");
    }

    internal static IReadOnlyList<SqlBatchCommand> Prepare(Guid? playerId, CharacterDissolveRequest request,
        string payload, State state)
    {
        if (RevisionCommand.Replay(state.Receipts.FirstOrDefault(), Operation, payload) is { } replay) { return replay; }

        Character character = state.Characters.FirstOrDefault(row => row.Id == request.CharacterId)
            ?? throw new BusinessArgumentException(ApiMessageKeys.CharacterNotOwned);
        if (character.Kind == CharacterKind.Core) { throw new BusinessArgumentException(ApiMessageKeys.CharacterDissolveCore); }
        if (character.Locked) { throw new BusinessArgumentException(ApiMessageKeys.CharacterLocked); }
        if (state.Deployments.Any(row => row.CharacterId == character.Id))
        { throw new BusinessArgumentException(ApiMessageKeys.CharacterDissolveDeployed); }

        int honor = CharacterHonorRules.DissolveReward(character.Level);
        string? releasedWeapon = string.IsNullOrEmpty(character.WeaponEntryKey) ? null : character.WeaponEntryKey;

        var writes = new SqlWriteBatch(playerId);
        CharacterRemoval.AddWrites(writes, character);
        long next = RevisionCommand.AdvanceRows(writes, state.Revisions, Domain, [request.CharacterId.ToString("D")]);
        var receipt = new CommandReceipt<CharacterDissolveSnapshot>
        {
            RequestId = request.RequestId, Revision = next,
            Snapshot = new()
            {
                CharacterId = character.Id, Level = character.Level, HonorGained = honor,
                ReleasedWeaponEntryId = releasedWeapon,
            },
        };
        writes.Add(ItemService.AdjustmentSql + """
            DECLARE @Result nvarchar(max) = JSON_MODIFY(@Receipt, '$.snapshot.honor_count', @Amount);
            INSERT INTO EconomyRequest (Id, CreateDate, CreateUserId, Status, RequestId, Operation, Payload, Result)
            VALUES (NEWID(), @CreateDate, @CreateUserId, @Status, @RequestId, @Operation, @Payload, @Result);
            SELECT @Result;
            """, new
        {
            ItemKey = CharacterHonorRules.ItemKey,
            Delta = honor,
            Category = (string?)null,
            NewId = Guid.NewGuid(),
            CreateDate = DateTime.Now,
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
