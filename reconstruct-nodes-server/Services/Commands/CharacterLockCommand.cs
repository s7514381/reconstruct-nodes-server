using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 鎖定／解鎖角色：整體覆寫 Locked。沿用「單次批次讀取 + 單次批次寫入並 COMMIT」流程與 EconomyRequest 回執：
/// 同一 request_id 重送回原收據。鎖定的角色由解散與升級素材檢查拒絕（CharacterRemoval 的呼叫端），核心不可鎖定。
/// </summary>
public class CharacterLockCommand(BaseServiceArgument argument) : BaseService(argument)
{
    private const string Operation = "character.set_locked";
    private static readonly string Domain = nameof(CharacterModel);

    internal sealed class State
    {
        public List<EconomyRequest> Receipts { get; set; } = [];
        public List<PlayerParams> Revisions { get; set; } = [];
        public List<Character> Characters { get; set; } = [];
    }

    public async Task<CommandReceipt<CharacterLockSnapshot>> Execute(CharacterLockRequest request)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(RevisionCommand.ReceiptsRead(rows => state.Receipts = rows));
        reads.Add(RevisionCommand.RevisionsRead(rows => state.Revisions = rows));
        reads.Add(OwnedSql.Rows<Character>("Id = @CharacterId", rows => state.Characters = rows));

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
        return JsonSerializer.Deserialize<CommandReceipt<CharacterLockSnapshot>>(json)
            ?? throw new InvalidOperationException("Invalid command receipt.");
    }

    internal static IReadOnlyList<SqlBatchCommand> Prepare(Guid? playerId, CharacterLockRequest request,
        string payload, State state)
    {
        if (RevisionCommand.Replay(state.Receipts.FirstOrDefault(), Operation, payload) is { } replay) { return replay; }

        Character character = state.Characters.FirstOrDefault(row => row.Id == request.CharacterId)
            ?? throw new BusinessArgumentException(ApiMessageKeys.CharacterNotOwned);
        if (character.Kind == CharacterKind.Core) { throw new BusinessArgumentException(ApiMessageKeys.CharacterLockCore); }

        var writes = new SqlWriteBatch(playerId);
        writes.Add($"UPDATE {OwnedSql.Table<Character>()} SET Locked = @Locked WHERE {OwnedSql.Where("Id = @Id")};",
            new { Id = character.Id, request.Locked });
        long next = RevisionCommand.AdvanceRows(writes, state.Revisions, Domain, [request.CharacterId.ToString("D")]);
        RevisionCommand.CompleteReceipt(writes, request, Operation, payload, next, new CharacterLockSnapshot
        {
            CharacterId = character.Id, Locked = request.Locked,
        });
        return writes.Commands;
    }
}
