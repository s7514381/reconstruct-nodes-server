using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

public class WeaponCommand(BaseServiceArgument argument, RevisionCommand revisions,
    WeaponService weapons) : BaseService(argument)
{
    private const string Operation = "weapon.disassemble";
    private static readonly string Domain = nameof(WeaponInstanceModel);
    public Task<CommandReceipt<WeaponInstanceModel>> Acquire(WeaponAcquireRequest request) =>
        revisions.ExecuteIntent(request, "weapon.acquire", nameof(WeaponInstanceModel), request, async () =>
        {
            if (request.Weapon == null) { throw new BusinessArgumentException(ApiMessageKeys.RequestInvalid); }
            await weapons.SaveForCommand(request.Weapon, acquire: true);
            return (await weapons.GetInstanceList()).Single(w => w.Id == request.Weapon.Id);
        });

    public Task<CommandReceipt<WeaponRefineSnapshot>> Refine(WeaponRefineRequest request) =>
        weapons.RefineForCommand(request);

    public async Task<CommandReceipt<WeaponDisassembleSnapshot>> Disassemble(WeaponDisassembleRequest request)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        if (request.WeaponEntryIds == null || request.WeaponEntryIds.Count > 500 ||
            request.WeaponEntryIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 450))
        { throw new BusinessArgumentException(ApiMessageKeys.RequestInvalid); }
        // A batch is a set: reordered or duplicate IDs are still the same intent.
        request = new WeaponDisassembleRequest
        {
            RequestId = request.RequestId,
            WeaponEntryIds = request.WeaponEntryIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
        };
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(OwnedSql.OwnerRead("Weapon"));
        reads.Add(RevisionCommand.ReceiptsRead(rows => state.Receipts = rows));
        reads.Add(RevisionCommand.RevisionsRead(rows => state.Revisions = rows));
        reads.Add(OwnedSql.Rows<Character>("WeaponEntryKey IS NOT NULL", rows => state.Equipped = rows));
        var weaponsRead = weapons.GetInstanceListRead;
        reads.Add(new SnapshotRead<object?>(weaponsRead.Sql, async reader =>
        { state.Weapons = (await weaponsRead.Read(reader)).ToList(); return null; }));

        string payload = RevisionCommand.CanonicalPayload(request);
        string json = await _repository.ExecuteAtomicJsonAsync(reads.Sql, new
        {
            CreateUserId = _playerId, request.RequestId,
            RevisionNames = RevisionCommand.RevisionNames(Domain, request.WeaponEntryIds),
        }, async reader =>
        {
            await reads.ReadAsync(reader);
            return PrepareDisassemble(_playerId, request, payload, state);
        });
        return JsonSerializer.Deserialize<CommandReceipt<WeaponDisassembleSnapshot>>(json)
            ?? throw new InvalidOperationException("Invalid command receipt.");
    }

    internal sealed class State
    {
        public List<EconomyRequest> Receipts { get; set; } = [];
        public List<PlayerParams> Revisions { get; set; } = [];
        public List<Character> Equipped { get; set; } = [];
        public List<WeaponInstanceModel> Weapons { get; set; } = [];
    }

    internal static IReadOnlyList<SqlBatchCommand> PrepareDisassemble(Guid? playerId,
        WeaponDisassembleRequest request, string payload, State state)
    {
        if (RevisionCommand.Replay(state.Receipts.FirstOrDefault(), Operation, payload) is { } replay) { return replay; }

        var owned = state.Weapons.Select(w => w.Id).ToHashSet();
        var equipped = state.Equipped.Select(c => c.WeaponEntryKey).ToHashSet();
        WeaponDisassembleSnapshot result = new();
        // Client semantics: skip protected/missing entries, with no material or currency reward.
        foreach (string id in request.WeaponEntryIds)
        {
            if (!owned.Contains(id) || equipped.Contains(id)) { result.SkippedIds.Add(id); }
            else { result.RemovedIds.Add(id); }
        }

        var writes = new SqlWriteBatch(playerId);
        if (result.RemovedIds.Count > 0)
        {
            // 只軟刪除父列；子列因父列 Status 被過濾，與 DeleteForCommand 相同。
            writes.Add($"""
                UPDATE {OwnedSql.Table<Weapon>()} SET Status = -1
                WHERE {OwnedSql.Where("EntryKey IN (SELECT [value] FROM OPENJSON(@Removed))")};
                """, new { Removed = JsonSerializer.Serialize(result.RemovedIds) });
        }
        long next = RevisionCommand.AdvanceRows(writes, state.Revisions, Domain, result.RemovedIds);
        result.Weapons = state.Weapons.Where(w => !result.RemovedIds.Contains(w.Id)).ToList();
        foreach (WeaponInstanceModel weapon in result.Weapons) { weapon.Revision = next; }
        RevisionCommand.CompleteReceipt(writes, request, Operation, payload, next, result);
        return writes.Commands;
    }
}
