using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 部署、戰場編輯與手動切波：沿用 WaveComplete 的「單次批次讀取 + 單次批次寫入並 COMMIT」流程。
/// 進度覆寫、部署費、revision、自動開波（active_wave 與 wave_start 子回執）及最終回執都在同一個寫入批次，固定 2 次 SQL 往返。
/// </summary>
public class BattlefieldCommand(BaseServiceArgument argument) : BaseService(argument)
{
    private static readonly string Domain = nameof(GameProgressModel);

    internal sealed class State
    {
        public List<EconomyRequest> Receipts { get; set; } = [];
        public GameProgressCommand.ProgressRows Progress { get; set; } = new();
        public List<Guid> Characters { get; set; } = [];
    }

    /// <summary>Change 回傳是否需要接著自動開波；Snapshot 由寫入後進度與開波結果組成回執內容。</summary>
    internal sealed record Plan<T>(string Operation, Func<GameProgressModel, bool> Change,
        Func<GameProgressModel, WaveStartResult?, T> Snapshot);

    internal static Plan<GameProgressModel> DeploymentPlan(DeploymentCommandRequest request) =>
        new("foreground.deployment", model =>
        {
            model.DeploymentLog = request.DeploymentLog;
            BattlefieldRules.Validate(model.Battlefield, model.DeploymentLog);
            return false;
        }, (progress, _) => progress);

    internal static Plan<WaveTransitionSnapshot> BattlefieldPlan(BattlefieldCommandRequest request) =>
        new("foreground.battlefield", model =>
        {
            if (request.Battlefield == null) { throw new BusinessArgumentException(ApiMessageKeys.ProgressCastleInvalid); }
            model.Battlefield = request.Battlefield;
            if (request.DeploymentLog != null) { model.DeploymentLog = request.DeploymentLog; }
            BattlefieldRules.Validate(model.Battlefield, model.DeploymentLog);
            model.WaveCompleted = false;
            return true;
        }, Transition);

    internal static Plan<WaveTransitionSnapshot> TransitionPlan(WaveTransitionRequest request) =>
        new("foreground.wave_transition", model =>
        {
            if (request.Difficulty is < 0 or > 3 || request.Wave < 1 || request.Difficulty != 3 && request.Wave > 100)
            { throw new BusinessArgumentException(ApiMessageKeys.EconomyWaveInvalid); }
            int cleared = model.ClearedWaves.Count > request.Difficulty &&
                model.ClearedWaves[request.Difficulty].TryGetInt32(out int value) ? value : 0;
            if (request.Challenge ? request.Wave != 1 : request.Wave > cleared)
            { throw new BusinessOperationException(request.Challenge ? "wave.challenge_first" : "wave.cleared_only"); }
            if (request.Difficulty == 3 && model.Difficulty != 3)
            {
                if (model.Difficulty is < 0 or > 2) { throw new BusinessOperationException(ApiMessageKeys.EconomyWaveInvalid); }
                int[] steps = [2, 4, 8];
                model.InfiniteLevel = 1 + (Math.Max(1, model.Wave) - 1) / steps[model.Difficulty];
            }
            model.Wave = request.Wave;
            model.Difficulty = request.Difficulty;
            model.MaxWaveReached = Math.Max(model.MaxWaveReached, model.Wave);
            model.WaveCompleted = false;
            return true;
        }, Transition);

    private static WaveTransitionSnapshot Transition(GameProgressModel progress, WaveStartResult? session) =>
        new() { Progress = progress, Session = session! };

    public Task<CommandReceipt<GameProgressModel>> SetDeployment(DeploymentCommandRequest request) =>
        Run(request, DeploymentPlan(request));

    public Task<CommandReceipt<WaveTransitionSnapshot>> SetBattlefield(BattlefieldCommandRequest request) =>
        Run(request, BattlefieldPlan(request));

    public Task<CommandReceipt<WaveTransitionSnapshot>> TransitionWave(WaveTransitionRequest request) =>
        Run(request, TransitionPlan(request));

    private async Task<CommandReceipt<T>> Run<T>(ExpectedRevisionRequest request, Plan<T> plan)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(OwnedSql.OwnerRead("Progress"));
        reads.Add(RevisionCommand.ReceiptsRead(rows => state.Receipts = rows));
        var progressRead = GameProgressCommand.StateRead("active_wave");
        reads.Add(new SnapshotRead<object?>(progressRead.Sql, async reader =>
        { state.Progress = await progressRead.Read(reader); return null; }));
        var charactersRead = GameProgressCommand.DeployableCharactersRead;
        reads.Add(new SnapshotRead<object?>(charactersRead.Sql, async reader =>
        { state.Characters = await charactersRead.Read(reader); return null; }));

        string payload = JsonSerializer.Serialize(request, request.GetType());
        string json = await _repository.ExecuteAtomicJsonAsync(reads.Sql,
            new { CreateUserId = _playerId, request.RequestId }, async reader =>
        {
            await reads.ReadAsync(reader);
            return Prepare(_playerId, request, payload, state, plan);
        });
        return JsonSerializer.Deserialize<CommandReceipt<T>>(json)
            ?? throw new InvalidOperationException("Invalid command receipt.");
    }

    internal static IReadOnlyList<SqlBatchCommand> Prepare<T>(Guid? playerId, ExpectedRevisionRequest request,
        string payload, State state, Plan<T> plan)
    {
        if (RevisionCommand.Replay(state.Receipts.FirstOrDefault(), plan.Operation, payload) is { } replay)
        { return replay; }

        var rows = state.Progress;
        long current = RevisionCommand.CurrentValue(rows.Parameters, Domain);
        RevisionCommand.CheckValues(Domain, request.ExpectedRevision, current, current);
        GameProgressModel model = GameProgressCommand.BuildFrom(rows, current)
            ?? throw new BusinessOperationException(ApiMessageKeys.ProgressMissing);
        bool startWave = plan.Change(model);

        GameProgressCommand.ValidateProgress(model);
        GameProgressCommand.ValidateCharacters(model.DeploymentLog ?? [], state.Characters.ToHashSet());
        int added = GameProgressCommand.NewDeploymentCount(model, rows.Deployments);
        PlayerParams? goldRow = rows.Parameters.SingleOrDefault(row => row.Group == "game_progress" && row.Name == "gold");
        var account = new GoldCommand.GoldBalance(goldRow == null ? GoldCommand.StartingGold : GoldCommand.ReadBalance(goldRow));
        if (added > 0) { GoldCommand.Spend(account, checked(added * GameProgressCommand.DeploymentCost)); }

        var writes = new SqlWriteBatch(playerId);
        long next = RevisionCommand.AdvanceRows(writes, rows.Parameters, Domain);
        GameProgressModel saved = GameProgressCommand.QueueReplace(writes, rows, model, next, account.Balance);
        // 舊版每次進度儲存都會建立金幣列；只有列不存在或餘額改變時才需要寫。
        string balance = account.Balance.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (goldRow == null)
        { writes.Insert([new PlayerParams { Group = "game_progress", Name = "gold", Value = balance }]); }
        else if (goldRow.Value != balance) { goldRow.Value = balance; writes.Update([goldRow]); }

        WaveStartResult? session = startWave
            ? EconomyCommand.QueueWaveStart(writes, rows.Parameters, ChildRequestId(request.RequestId, "wave"),
                model.Wave, model.Difficulty, model.InfiniteLevel, account.Balance)
            : null;
        RevisionCommand.CompleteReceipt(writes, request, plan.Operation, payload, next, plan.Snapshot(saved, session));
        return writes.Commands;
    }

    internal static Guid ChildRequestId(Guid id, string operation) =>
        new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{id:D}/{operation}")).AsSpan(0, 16));
}
