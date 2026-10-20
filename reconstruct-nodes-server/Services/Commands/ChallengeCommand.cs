using System.Globalization;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 挑戰關卡：讀取今日剩餘次數，並在最後一波通關後發放通關獎勵。
/// 進入挑戰扣次數在 <see cref="BattlefieldCommand"/> 的 TransitionWave 交易內完成；
/// 這裡的領取只認伺服器進度（目前在挑戰關卡且最後一波已通關），每次進入只發一次：
/// 單一原子批次以「次數列內容不變」的條件更新標記已領取，並與獎勵物品、回條同交易寫入，
/// 並行或重複請求只會成功一次，同 request_id 重送重播原回條。
/// </summary>
public class ChallengeCommand(BaseServiceArgument argument, TimeProvider clock) : BaseService(argument)
{
    private const string Operation = "challenge.claim";
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    internal sealed class State
    {
        public List<EconomyRequest> Receipts { get; set; } = [];
        public List<PlayerParams> Parameters { get; set; } = [];
        public List<Item> Items { get; set; } = [];
    }

    public async Task<ChallengeStatus> GetStatus()
    {
        DateTime today = clock.GetLocalNow().DateTime;
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(OwnedSql.OwnerRead("ChallengeStatus"));
        reads.Add(OwnedSql.Rows<PlayerParams>(ChallengeRules.AttemptRowFilter, rows => state.Parameters = rows));
        await using (var reader = await _repository.QueryMultipleAsync(reads.Sql, new { CreateUserId = _playerId }))
        {
            await reads.ReadAsync(reader);
        }
        return ChallengeRules.BuildStatus(state.Parameters, DailyCheckInCatalog.DateKey(today));
    }

    public async Task<ChallengeRewardReceipt> Claim(ChallengeClaimRequest request)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        DateTime today = clock.GetLocalNow().DateTime;
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(OwnedSql.OwnerRead("Challenge"));
        reads.Add(OwnedSql.Rows<EconomyRequest>("RequestId = @RequestId", rows => state.Receipts = rows, activeOnly: false));
        reads.Add(OwnedSql.Rows<PlayerParams>(
            $"([Group] = 'game_progress' AND Name IN ('difficulty', 'wave', 'wave_completed')) OR ({ChallengeRules.AttemptRowFilter})",
            rows => state.Parameters = rows));
        reads.Add(OwnedSql.Rows<Item>($"Amount > 0 OR ItemKey = '{ChallengeRules.RewardItemKey}'", rows => state.Items = rows));
        string payload = RevisionCommand.CanonicalPayload(new { });
        await using (var reader = await _repository.QueryMultipleAsync(reads.Sql,
            new { CreateUserId = _playerId, request.RequestId }))
        {
            await reads.ReadAsync(reader);
        }

        EconomyRequest? prior = state.Receipts.FirstOrDefault(r => r.RequestId == request.RequestId);
        if (prior != null)
        {
            if (prior.Operation != Operation || prior.Payload != payload)
            { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
            return JsonSerializer.Deserialize<ChallengeRewardReceipt>(prior.Result)
                ?? throw new InvalidOperationException("Invalid challenge receipt.");
        }

        SqlBatchCommand write = PrepareWrite(_playerId, request, payload, state, today);
        try
        {
            string json = await _repository.QueryFirstAsync<string>(write.Sql, write.Parameters);
            return JsonSerializer.Deserialize<ChallengeRewardReceipt>(json)
                ?? throw new InvalidOperationException("Invalid challenge receipt.");
        }
        catch (System.Data.SqlClient.SqlException error)
            when (error.Number is UniqueIndexViolation or UniqueConstraintViolation or ItemRewardBatch.RewardRowError)
        {
            // 並行請求已先提交同一次通關或同一請求：用戶端重新讀取狀態或以原請求重送。
            throw new BusinessOperationException(ApiMessageKeys.EconomyRequestConflict);
        }
    }

    internal static SqlBatchCommand PrepareWrite(Guid? playerId, ChallengeClaimRequest request, string payload,
        State state, DateTime today)
    {
        int Scalar(string name) => int.Parse(
            state.Parameters.SingleOrDefault(p => p.Group == "game_progress" && p.Name == name)?.Value ?? "0",
            CultureInfo.InvariantCulture);
        int difficulty = Scalar("difficulty"), wave = Scalar("wave");
        bool completed = bool.TryParse(
            state.Parameters.SingleOrDefault(p => p.Group == "game_progress" && p.Name == "wave_completed")?.Value, out bool flag) && flag;
        // 伺服器進度必須顯示目前挑戰的最後一波已通關（WaveComplete 交易寫入）。
        if (!StageRules.IsChallenge(difficulty) || !StageRules.IsStageFinished(difficulty, wave, completed))
        { throw new BusinessOperationException(ApiMessageKeys.ChallengeNotCleared); }

        PlayerParams attemptRow = ChallengeRules.FindAttemptRow(state.Parameters, difficulty)
            ?? throw new BusinessOperationException(ApiMessageKeys.ChallengeNotCleared);
        ChallengeRules.Attempts attempts = ChallengeRules.Attempts.Parse(attemptRow.Value)
            ?? throw new BusinessOperationException(ApiMessageKeys.ChallengeNotCleared);
        if (attempts.Rewarded) { throw new BusinessOperationException(ApiMessageKeys.ChallengeAlreadyRewarded); }

        string todayKey = DailyCheckInCatalog.DateKey(today);
        var writes = new SqlWriteBatch(playerId);
        writes.Add(ItemRewardBatch.BeginTransaction);
        writes.Add($"""
            UPDATE PlayerParams SET Value = @Value
            WHERE Id = @RowId AND CreateUserId = @CreateUserId AND Status != -1 AND Value = @OldValue;
            IF @@ROWCOUNT <> 1 THROW {ItemRewardBatch.RewardRowError}, N'0', 1;
            """, new
        {
            Value = new ChallengeRules.Attempts { Date = attempts.Date, Used = attempts.Used, Rewarded = true }.Serialize(),
            RowId = attemptRow.Id, OldValue = attemptRow.Value,
        });
        return ItemRewardBatch.Complete(writes, state.Items, ChallengeRules.RewardItemKey, ChallengeRules.RewardAmount,
            inStock => new ChallengeRewardReceipt
            {
                RequestId = request.RequestId, Difficulty = difficulty, Date = todayKey,
                ItemId = ChallengeRules.RewardItemKey, Amount = ChallengeRules.RewardAmount, Items = inStock,
            }, today, request.RequestId, Operation, payload);
    }
}
