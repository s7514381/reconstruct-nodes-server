using System.Globalization;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 天賦指令：沿用 WaveComplete 的「單次批次讀取 + 單次批次寫入並 COMMIT」流程。
/// 原本每個節點各自 INSERT、revision 與回執也逐筆往返（約 20+ 次 SQL），現在固定 2 次。
/// </summary>
public class TalentCommand(BaseServiceArgument argument) : BaseService(argument)
{
    private static readonly string Domain = nameof(TalentModel);

    private sealed class State
    {
        public List<EconomyRequest> Receipts { get; set; } = [];
        public List<PlayerParams> Revisions { get; set; } = [];
        public List<Talent> Parents { get; set; } = [];
        public List<TalentAcquired> Acquired { get; set; } = [];
        public List<TalentLegacyCategory> Legacy { get; set; } = [];
    }

    public Task<CommandReceipt<TalentModel>> Learn(TalentNodeCommandRequest request) =>
        Change(request, "foreground.talent_learn", model => TalentRules.Learn(model, request.TreeId, request.NodeId));
    public Task<CommandReceipt<TalentModel>> RefundNode(TalentNodeCommandRequest request) =>
        Change(request, "foreground.talent_refund", model => TalentRules.RefundNode(model, request.TreeId, request.NodeId));
    public Task<CommandReceipt<TalentModel>> Reset(TalentResetRequest request) =>
        Change(request, "foreground.talent_reset", model => TalentRules.Reset(model, request.TreeId));

    private async Task<CommandReceipt<TalentModel>> Change(ExpectedRevisionRequest request, string operation,
        Action<TalentModel> change)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(OwnedSql.Rows<EconomyRequest>("RequestId = @RequestId", rows => state.Receipts = rows, activeOnly: false));
        reads.Add(OwnedSql.Rows<PlayerParams>("[Group] = @RevisionGroup AND Name = @Domain", rows => state.Revisions = rows));
        reads.Add(new SnapshotRead<object?>(OwnedSql.Select<Talent>(top: 1), async reader =>
        { state.Parents = (await reader.ReadAsync<Talent>()).ToList(); return null; }));
        reads.Add(OwnedSql.ChildrenRows<TalentAcquired, Talent>(rows => state.Acquired = rows));
        reads.Add(OwnedSql.ChildrenRows<TalentLegacyCategory, Talent>(rows => state.Legacy = rows));

        string payload = JsonSerializer.Serialize(request, request.GetType());
        string json = await _repository.ExecuteAtomicJsonAsync(reads.Sql, new
        {
            CreateUserId = _playerId, request.RequestId, RevisionGroup = RevisionCommand.RevisionGroup, Domain,
        }, async reader =>
        {
            await reads.ReadAsync(reader);
            return Prepare(request, operation, payload, change, state);
        });
        return JsonSerializer.Deserialize<CommandReceipt<TalentModel>>(json)
            ?? throw new InvalidOperationException("Invalid command receipt.");
    }

    private IReadOnlyList<SqlBatchCommand> Prepare(ExpectedRevisionRequest request, string operation, string payload,
        Action<TalentModel> change, State state)
    {
        var prior = state.Receipts.FirstOrDefault();
        if (prior != null)
        {
            if (prior.Operation != operation || prior.Payload != payload)
            { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
            return [new SqlBatchCommand("SELECT @Receipt;", new { Receipt = prior.Result })];
        }

        PlayerParams? revisionRow = state.Revisions.FirstOrDefault();
        long current = revisionRow == null ? 0 : long.Parse(revisionRow.Value, CultureInfo.InvariantCulture);
        RevisionCommand.CheckValues(Domain, request.ExpectedRevision, current, current);

        Talent talent = state.Parents.FirstOrDefault()
            ?? throw new BusinessOperationException(ApiMessageKeys.TalentMissing);
        TalentModel model = TalentService.BuildSnapshot(talent, state.Acquired, state.Legacy, current);
        change(model);
        TalentService.ValidateTalent(model);

        talent.TalentPoints = model.TalentPoints;
        talent.TotalSpentPoints = model.TotalSpentPoints;
        List<TalentAcquired> acquired = ToRows(model.AcquiredTalents, (tree, node, value) =>
            new TalentAcquired { TalentId = talent.Id, TreeId = tree, NodeId = node, Value = value });
        List<TalentLegacyCategory> legacy = ToRows(model.LegacyCategoryTalents, (tree, node, value) =>
            new TalentLegacyCategory { TalentId = talent.Id, TreeId = tree, NodeId = node, Value = value });

        long next = checked(current + 1);
        var receipt = new CommandReceipt<TalentModel>
        {
            RequestId = request.RequestId, Revision = next,
            Snapshot = TalentService.BuildSnapshot(talent, acquired, legacy, next),
        };
        string resultJson = JsonSerializer.Serialize(receipt);

        var writes = new SqlWriteBatch(_playerId);
        writes.Update([talent]);
        writes.Add($"""
            {OwnedSql.Delete<TalentAcquired>("TalentId = @TalentId")}
            {OwnedSql.Delete<TalentLegacyCategory>("TalentId = @TalentId")}
            """, new { TalentId = talent.Id });
        writes.Insert(acquired);
        writes.Insert(legacy);
        if (revisionRow == null)
        {
            writes.Insert([new PlayerParams { Group = RevisionCommand.RevisionGroup, Name = Domain,
                Value = next.ToString(CultureInfo.InvariantCulture) }]);
        }
        else
        {
            revisionRow.Value = next.ToString(CultureInfo.InvariantCulture);
            writes.Update([revisionRow]);
        }
        writes.Insert([new EconomyRequest { RequestId = request.RequestId, Operation = operation,
            Payload = payload, Result = resultJson }]);
        writes.Add("SELECT @Result;", new { Result = resultJson });
        return writes.Commands;
    }

    private static List<TRow> ToRows<TRow>(Dictionary<string, Dictionary<string, JsonElement>>? trees,
        Func<string, string, string, TRow> create) where TRow : ModelBase
    {
        List<TRow> rows = [];
        foreach ((string tree, Dictionary<string, JsonElement> nodes) in trees ?? [])
        {
            foreach ((string node, JsonElement value) in nodes)
            {
                TRow row = create(tree, node, value.GetRawText());
                row.Sequence = rows.Count;
                rows.Add(row);
            }
        }
        return rows;
    }
}
