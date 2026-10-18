using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 每日簽到：每月累計簽到 <see cref="DailyCheckInCatalog.RequiredDays"/> 天即達成，第 n 次簽到領第 n 格獎勵。
/// 日期一律取伺服器本地日期。領取比照內購：單一原子批次寫入簽到紀錄、獎勵物品（既有列相對加）與回條；
/// (玩家, DateKey) 唯一索引保證並行請求同一天只成功一次，同 request_id 重送重播原回執。
/// </summary>
public class DailyCheckInCommand(BaseServiceArgument argument, TimeProvider clock) : BaseService(argument)
{
    private const string Operation = "checkin.claim";
    private const int RewardRowError = 51031;
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    private const string BeginTransaction = "SET XACT_ABORT ON; BEGIN TRAN;";

    private const string PersistReceipt = """
        INSERT INTO EconomyRequest (Id, CreateDate, CreateUserId, Status, RequestId, Operation, Payload, Result)
        VALUES (NEWID(), @Now, @CreateUserId, @Enabled, @RequestId, @Operation, @Payload, @Result);
        COMMIT;
        SELECT @Result;
        """;

    internal sealed class State
    {
        public List<EconomyRequest> Receipts { get; set; } = [];
        public List<DailyCheckIn> Month { get; set; } = [];
        public List<Item> Items { get; set; } = [];
    }

    public async Task<DailyCheckInStatus> GetStatus()
    {
        DateTime today = clock.GetLocalNow().DateTime;
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(OwnedSql.OwnerRead("DailyCheckInStatus"));
        reads.Add(MonthRead(state));
        await using (var reader = await _repository.QueryMultipleAsync(reads.Sql,
            new { CreateUserId = _playerId, MonthPrefix = DailyCheckInCatalog.MonthKey(today) + "-%" }))
        {
            await reads.ReadAsync(reader);
        }
        return BuildStatus(today, state.Month);
    }

    public async Task<DailyCheckInReceipt> Claim(DailyCheckInClaimRequest request)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        DateTime today = clock.GetLocalNow().DateTime;
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(OwnedSql.OwnerRead("DailyCheckIn"));
        reads.Add(OwnedSql.Rows<EconomyRequest>("RequestId = @RequestId", rows => state.Receipts = rows, activeOnly: false));
        reads.Add(MonthRead(state));
        reads.Add(OwnedSql.Rows<Item>($"Amount > 0 OR ItemKey IN ('{DailyCheckInCatalog.DiamondItemKey}', '{DailyCheckInCatalog.EnergyExtractorItemKey}')",
            rows => state.Items = rows));
        string payload = RevisionCommand.CanonicalPayload(new { });
        await using (var reader = await _repository.QueryMultipleAsync(reads.Sql,
            new { CreateUserId = _playerId, request.RequestId, MonthPrefix = DailyCheckInCatalog.MonthKey(today) + "-%" }))
        {
            await reads.ReadAsync(reader);
        }

        EconomyRequest? prior = state.Receipts.FirstOrDefault(r => r.RequestId == request.RequestId);
        if (prior != null)
        {
            if (prior.Operation != Operation || prior.Payload != payload)
            { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
            return JsonSerializer.Deserialize<DailyCheckInReceipt>(prior.Result)
                ?? throw new InvalidOperationException("Invalid check-in receipt.");
        }

        SqlBatchCommand write = PrepareWrite(_playerId, request, payload, state, today);
        try
        {
            string json = await _repository.QueryFirstAsync<string>(write.Sql, write.Parameters);
            return JsonSerializer.Deserialize<DailyCheckInReceipt>(json)
                ?? throw new InvalidOperationException("Invalid check-in receipt.");
        }
        catch (System.Data.SqlClient.SqlException error)
            when (error.Number is UniqueIndexViolation or UniqueConstraintViolation or RewardRowError)
        {
            // 並行請求已先提交同一天或同一請求：用戶端重新讀取狀態或以原請求重送。
            throw new BusinessOperationException(ApiMessageKeys.EconomyRequestConflict);
        }
    }

    private static SnapshotRead<List<DailyCheckIn>> MonthRead(State state) =>
        OwnedSql.Rows<DailyCheckIn>("DateKey LIKE @MonthPrefix", rows => state.Month = rows, orderBy: "Ordinal");

    internal static DailyCheckInStatus BuildStatus(DateTime today, List<DailyCheckIn> month)
    {
        string todayKey = DailyCheckInCatalog.DateKey(today);
        return new DailyCheckInStatus
        {
            Month = DailyCheckInCatalog.MonthKey(today),
            Today = todayKey,
            RequiredDays = DailyCheckInCatalog.RequiredDays,
            Claimed = month.Select(row => new DailyCheckInClaimedModel { Date = row.DateKey, Ordinal = row.Ordinal }).ToList(),
            Rewards = DailyCheckInCatalog.All
                .Select((reward, index) => new DailyCheckInRewardModel { Ordinal = index + 1, ItemId = reward.ItemKey, Amount = reward.Amount })
                .ToList(),
            CanClaim = month.Count < DailyCheckInCatalog.RequiredDays && month.All(row => row.DateKey != todayKey),
        };
    }

    internal static SqlBatchCommand PrepareWrite(Guid? playerId, DailyCheckInClaimRequest request, string payload,
        State state, DateTime today)
    {
        string todayKey = DailyCheckInCatalog.DateKey(today);
        if (state.Month.Any(row => row.DateKey == todayKey)) { throw new BusinessOperationException(ApiMessageKeys.CheckInAlreadyClaimed); }
        if (state.Month.Count >= DailyCheckInCatalog.RequiredDays) { throw new BusinessOperationException(ApiMessageKeys.CheckInMonthComplete); }

        int ordinal = state.Month.Count + 1;
        DailyCheckInCatalog.Reward grant = DailyCheckInCatalog.RewardFor(ordinal);
        var writes = new SqlWriteBatch(playerId);
        writes.Add(BeginTransaction);
        writes.Insert([new DailyCheckIn
        {
            DateKey = todayKey, Ordinal = ordinal, RequestId = request.RequestId,
            ItemKey = grant.ItemKey, Amount = grant.Amount,
        }]);

        // 與內購相同語意：只認未刪除的列；沒有就新增，既有列以 SQL 相對加上避免覆蓋並行的物品同步。
        Item? reward = state.Items.FirstOrDefault(i => i.ItemKey == grant.ItemKey);
        bool rewardExists = reward != null;
        if (reward == null)
        {
            reward = new Item { ItemKey = grant.ItemKey, Amount = grant.Amount, Category = "" };
            state.Items.Add(reward);
            writes.Insert([reward]);
        }
        else
        {
            reward.Amount = checked(reward.Amount + grant.Amount);
        }

        List<Item> inStock = state.Items.Where(i => i.Amount > 0).ToList();
        var receipt = new DailyCheckInReceipt
        {
            RequestId = request.RequestId, Date = todayKey, Ordinal = ordinal,
            ItemId = grant.ItemKey, Amount = grant.Amount, Items = inStock,
        };
        string fragment = PersistReceipt;
        if (rewardExists)
        {
            int index = inStock.IndexOf(reward);
            fragment = $"""
                DECLARE @Amount int;
                UPDATE Item SET @Amount = Amount = Amount + @Reward
                WHERE Id = @ItemId AND CreateUserId = @CreateUserId AND Status != -1;
                IF @@ROWCOUNT <> 1 THROW {RewardRowError}, N'0', 1;
                SET @Result = JSON_MODIFY(@Result, '$.items[{index}].Amount', @Amount);
                {PersistReceipt}
                """;
        }
        writes.Add(fragment, new
        {
            Reward = grant.Amount, ItemId = reward.Id, Result = JsonSerializer.Serialize(receipt), Now = today,
            Enabled = (int)Library.Models.Enum.EStatus.啟用,
            request.RequestId, Operation, Payload = payload,
        });
        IReadOnlyList<SqlBatchCommand> commands = writes.Commands;
        if (commands.Count != 1) { throw new InvalidOperationException("Check-in write must be one atomic batch."); }
        return commands[0];
    }
}
