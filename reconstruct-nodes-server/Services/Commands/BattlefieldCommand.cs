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
public class BattlefieldCommand(BaseServiceArgument argument, TimeProvider clock) : BaseService(argument)
{
    private static readonly string Domain = nameof(GameProgressModel);

    internal sealed class State
    {
        public List<EconomyRequest> Receipts { get; set; } = [];
        public GameProgressCommand.ProgressRows Progress { get; set; } = new();
        public List<Guid> Characters { get; set; } = [];
    }

    /// <summary>
    /// Change 取得目前進度與讀回的 PlayerParams 列（可原地修改附屬列），回傳是否需要接著自動開波；
    /// Persist 在進度寫入前把 Change 另外改動的附屬列加入同一個寫入批次；
    /// Snapshot 由寫入後進度與開波結果組成回執內容。
    /// </summary>
    internal sealed record Plan<T>(string Operation, Func<GameProgressModel, List<PlayerParams>, bool> Change,
        Func<GameProgressModel, WaveStartResult?, T> Snapshot, Action<SqlWriteBatch>? Persist = null);

    internal static Plan<GameProgressModel> DeploymentPlan(DeploymentCommandRequest request) =>
        new("foreground.deployment", (model, _) =>
        {
            model.DeploymentLog = request.DeploymentLog;
            BattlefieldRules.Validate(model.Battlefield, model.DeploymentLog);
            // 本請求不帶隊長：隊長若已不在部署中就清空，由前端依規則自動選擇。
            if (model.CaptainCharacterId is { } captain && model.DeploymentLog.All(row => row.CharacterId != captain))
            { model.CaptainCharacterId = null; }
            return false;
        }, (progress, _) => progress);

    internal static Plan<WaveTransitionSnapshot> BattlefieldPlan(BattlefieldCommandRequest request) =>
        new("foreground.battlefield", (model, _) =>
        {
            if (request.Battlefield == null) { throw new BusinessArgumentException(ApiMessageKeys.ProgressCastleInvalid); }
            model.Battlefield = request.Battlefield;
            if (request.DeploymentLog != null) { model.DeploymentLog = request.DeploymentLog; }
            BattlefieldRules.Validate(model.Battlefield, model.DeploymentLog);
            model.CaptainCharacterId = request.CaptainCharacterId;
            BattlefieldRules.ValidateCaptain(model.CaptainCharacterId, model.DeploymentLog);
            model.WaveCompleted = false;
            return true;
        }, Transition);

    /// <param name="today">伺服器本地時間，挑戰每日次數以此換日；非挑戰目標不使用。</param>
    internal static Plan<WaveTransitionSnapshot> TransitionPlan(WaveTransitionRequest request, DateTime today = default)
    {
        (PlayerParams Row, bool Created)? attempt = null;
        return new("foreground.wave_transition", (model, rows) =>
        {
            if (!StageRules.IsValidTarget(request.Difficulty, request.Wave))
            { throw new BusinessArgumentException(ApiMessageKeys.EconomyWaveInvalid); }
            int cleared = model.ClearedWaves.Count > request.Difficulty &&
                model.ClearedWaves[request.Difficulty].TryGetInt32(out int value) ? value : 0;
            // 挑戰每次進入都要從第 1 波重新開始（並扣一次今日次數），不能以通關紀錄直接跳波。
            bool restartOnly = request.Challenge || StageRules.IsChallenge(request.Difficulty);
            if (restartOnly ? request.Wave != 1 || !request.Challenge : request.Wave > cleared)
            { throw new BusinessOperationException(restartOnly ? "wave.challenge_first" : "wave.cleared_only"); }
            bool inLimitedStage = StageRules.IsLimitedStage(model.Difficulty);
            if (StageRules.IsLimitedStage(request.Difficulty) && !inLimitedStage)
            {
                // 進試煉或挑戰時記下一般模式位置；限定關卡之間切換沿用原返回點。無限等級保持原值。
                model.TrialReturnDifficulty = model.Difficulty;
                model.TrialReturnWave = Math.Max(1, model.Wave);
            }
            else if (request.Difficulty == StageRules.Infinite && model.Difficulty != StageRules.Infinite)
            {
                // 無限等級來源是離開前的一般位置；在限定關卡中則以進入前的位置為準，原本在無限則不重算。
                int fromDifficulty = inLimitedStage ? model.TrialReturnDifficulty : model.Difficulty;
                int fromWave = inLimitedStage ? model.TrialReturnWave : model.Wave;
                if (fromDifficulty != StageRules.Infinite)
                {
                    if (fromDifficulty is < 0 or > 2) { throw new BusinessOperationException(ApiMessageKeys.EconomyWaveInvalid); }
                    model.InfiniteLevel = StageRules.EnemyLevel(fromDifficulty, fromWave, 1);
                }
            }
            if (StageRules.IsChallenge(request.Difficulty))
            { attempt = ChallengeRules.Consume(rows, request.Difficulty, DailyCheckInCatalog.DateKey(today)); }
            model.Wave = request.Wave;
            model.Difficulty = request.Difficulty;
            model.MaxWaveReached = Math.Max(model.MaxWaveReached, model.Wave);
            model.WaveCompleted = false;
            return true;
        }, Transition, writes =>
        {
            if (attempt is not { } used) { return; }
            if (used.Created) { writes.Insert([used.Row]); } else { writes.Update([used.Row]); }
        });
    }

    private static WaveTransitionSnapshot Transition(GameProgressModel progress, WaveStartResult? session) =>
        new() { Progress = progress, Session = session! };

    public Task<CommandReceipt<GameProgressModel>> SetDeployment(DeploymentCommandRequest request) =>
        Run(request, DeploymentPlan(request));

    public Task<CommandReceipt<WaveTransitionSnapshot>> SetBattlefield(BattlefieldCommandRequest request) =>
        Run(request, BattlefieldPlan(request));

    public Task<CommandReceipt<WaveTransitionSnapshot>> TransitionWave(WaveTransitionRequest request) =>
        Run(request, TransitionPlan(request, clock.GetLocalNow().DateTime));

    private async Task<CommandReceipt<T>> Run<T>(ExpectedRevisionRequest request, Plan<T> plan)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(OwnedSql.OwnerRead("Progress"));
        reads.Add(RevisionCommand.ReceiptsRead(rows => state.Receipts = rows));
        var progressRead = GameProgressCommand.StateRead(["active_wave", .. ChallengeRules.AttemptNames]);
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
        bool startWave = plan.Change(model, rows.Parameters);

        GameProgressCommand.ValidateProgress(model);
        GameProgressCommand.ValidateCharacters(model.DeploymentLog ?? [], state.Characters.ToHashSet());
        PlayerParams? goldRow = rows.Parameters.SingleOrDefault(row => row.Group == "game_progress" && row.Name == "gold");
        var account = new GoldCommand.GoldBalance(goldRow == null ? GoldCommand.StartingGold : GoldCommand.ReadBalance(goldRow));

        var writes = new SqlWriteBatch(playerId);
        plan.Persist?.Invoke(writes);
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
