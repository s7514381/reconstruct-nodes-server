using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 看廣告領鑽石的玩家端用例。廣告是否看完由 AdMob SSV 回呼（<see cref="AdRewardSsvService"/>）建立已驗證的
/// <see cref="AdRewardGrant"/>；玩家以同一 request_id 領取時才計入當日次數並發放。
/// 領取比照每日簽到：單一原子批次寫入領取標記、獎勵物品（既有列相對加）與回條，
/// (玩家, DateKey, Slot) 唯一索引保證並行請求不會超過每日上限，同 request_id 重送重播原回執。
/// </summary>
public class AdRewardCommand(BaseServiceArgument argument, TimeProvider clock) : BaseService(argument)
{
    private const string Operation = "ad_reward.claim";
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    internal sealed class State
    {
        public List<EconomyRequest> Receipts { get; set; } = [];
        public List<AdRewardGrant> ClaimedToday { get; set; } = [];
        public List<AdRewardGrant> Verified { get; set; } = [];
        public List<Item> Items { get; set; } = [];
    }

    public async Task<AdRewardStatus> GetStatus()
    {
        DateTime today = clock.GetLocalNow().DateTime;
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(OwnedSql.OwnerRead("AdRewardStatus"));
        reads.Add(ClaimedTodayRead(state));
        reads.Add(VerifiedRead(state));
        await using (var reader = await _repository.QueryMultipleAsync(reads.Sql,
            new { CreateUserId = _playerId, Today = DailyCheckInCatalog.DateKey(today) }))
        {
            await reads.ReadAsync(reader);
        }
        return BuildStatus(today, state, _playerId);
    }

    public async Task<AdRewardReceipt> Claim(AdRewardClaimRequest request)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        DateTime today = clock.GetLocalNow().DateTime;
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(OwnedSql.OwnerRead("AdRewardClaim"));
        reads.Add(OwnedSql.Rows<EconomyRequest>("RequestId = @RequestId", rows => state.Receipts = rows, activeOnly: false));
        reads.Add(ClaimedTodayRead(state));
        reads.Add(VerifiedRead(state));
        reads.Add(OwnedSql.Rows<Item>($"Amount > 0 OR ItemKey = '{AdRewardCatalog.DiamondItemKey}'", rows => state.Items = rows));
        string payload = RevisionCommand.CanonicalPayload(new { });
        await using (var reader = await _repository.QueryMultipleAsync(reads.Sql,
            new { CreateUserId = _playerId, request.RequestId, Today = DailyCheckInCatalog.DateKey(today) }))
        {
            await reads.ReadAsync(reader);
        }

        EconomyRequest? prior = state.Receipts.FirstOrDefault(r => r.RequestId == request.RequestId);
        if (prior != null)
        {
            if (prior.Operation != Operation || prior.Payload != payload)
            { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
            return JsonSerializer.Deserialize<AdRewardReceipt>(prior.Result)
                ?? throw new InvalidOperationException("Invalid ad reward receipt.");
        }

        SqlBatchCommand write = PrepareWrite(_playerId, request, payload, state, today);
        try
        {
            string json = await _repository.QueryFirstAsync<string>(write.Sql, write.Parameters);
            return JsonSerializer.Deserialize<AdRewardReceipt>(json)
                ?? throw new InvalidOperationException("Invalid ad reward receipt.");
        }
        catch (System.Data.SqlClient.SqlException error)
            when (error.Number is UniqueIndexViolation or UniqueConstraintViolation or ItemRewardBatch.RewardRowError)
        {
            // 並行請求已先提交同一個名額或同一請求：用戶端重新讀取狀態或以原請求重送。
            throw new BusinessOperationException(ApiMessageKeys.EconomyRequestConflict);
        }
    }

    private static SnapshotRead<List<AdRewardGrant>> ClaimedTodayRead(State state) =>
        OwnedSql.Rows<AdRewardGrant>("Claimed = 1 AND DateKey = @Today", rows => state.ClaimedToday = rows, orderBy: "Slot");

    private static SnapshotRead<List<AdRewardGrant>> VerifiedRead(State state) =>
        OwnedSql.Rows<AdRewardGrant>("Claimed = 0", rows => state.Verified = rows, orderBy: "CreateDate");

    internal static AdRewardStatus BuildStatus(DateTime today, State state, Guid? playerId) => new()
    {
        Today = DailyCheckInCatalog.DateKey(today),
        DailyLimit = AdRewardCatalog.DailyLimit,
        ClaimedToday = state.ClaimedToday.Count,
        ItemId = AdRewardCatalog.DiamondItemKey,
        SsvUserId = playerId?.ToString() ?? "",
        Amount = AdRewardCatalog.DiamondsPerAd,
        CanWatch = state.ClaimedToday.Count < AdRewardCatalog.DailyLimit,
        PendingRequestId = state.Verified.FirstOrDefault()?.RequestId.ToString() ?? "",
    };

    internal static SqlBatchCommand PrepareWrite(Guid? playerId, AdRewardClaimRequest request, string payload,
        State state, DateTime today)
    {
        AdRewardGrant grant = state.Verified.FirstOrDefault(row => row.RequestId == request.RequestId)
            ?? throw new BusinessOperationException(ApiMessageKeys.AdRewardPending);
        if (state.ClaimedToday.Count >= AdRewardCatalog.DailyLimit)
        { throw new BusinessOperationException(ApiMessageKeys.AdRewardLimitReached); }

        string todayKey = DailyCheckInCatalog.DateKey(today);
        int slot = state.ClaimedToday.Count + 1;
        int amount = AdRewardCatalog.DiamondsPerAd;
        grant.Claimed = true;
        grant.DateKey = todayKey;
        grant.Slot = slot;
        grant.Amount = amount;
        var writes = new SqlWriteBatch(playerId);
        writes.Add(ItemRewardBatch.BeginTransaction);
        writes.Update([grant]);
        return ItemRewardBatch.Complete(writes, state.Items, AdRewardCatalog.DiamondItemKey, amount,
            inStock => new AdRewardReceipt
            {
                RequestId = request.RequestId, Date = todayKey, Slot = slot,
                ItemId = AdRewardCatalog.DiamondItemKey, Amount = amount, Items = inStock,
            }, today, request.RequestId, Operation, payload);
    }
}
