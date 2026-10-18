using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Billing;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 驗證 Google Play 內購並發放鑽石。順序固定為：讀取（回條與 token 紀錄）→ 向 Google 驗證 → 單一原子寫入
/// （購買紀錄、鑽石、回條）→ consume。購買紀錄的 PurchaseToken 唯一索引保證同一筆購買只發放一次；
/// consume 失敗不回滾發放，回執以 Consumed=false 告知用戶端，之後以原請求重送即會重試 consume。
/// </summary>
public class PurchaseCommand(BaseServiceArgument argument, IGooglePlayBilling billing,
    ILogger<PurchaseCommand> logger) : BaseService(argument)
{
    private const string Operation = "iap.google_play";
    private const int MaxTokenLength = 450;
    private const int PurchasedState = 0;
    private const int ConsumedState = 1;
    private const int RewardRowError = 51021;
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
        public IapPurchase? Token { get; set; }
        public List<Item> Items { get; set; } = [];
    }

    public async Task<GooglePlayPurchaseReceipt> Execute(GooglePlayPurchaseRequest request)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        if (!PurchaseCatalog.TryGetDiamonds(request.ProductId, out int diamonds))
        {
            throw new BusinessArgumentException(ApiMessageKeys.PurchaseProductUnknown);
        }
        if (string.IsNullOrWhiteSpace(request.PurchaseToken) || request.PurchaseToken.Length > MaxTokenLength)
        {
            throw new BusinessArgumentException(ApiMessageKeys.PurchaseTokenInvalid);
        }

        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(OwnedSql.OwnerRead("Purchase"));
        // 同帳號以不同 request_id 重送同一 token 時，也要能取到原發放回條。
        reads.Add(OwnedSql.Rows<EconomyRequest>(
            $"RequestId = @RequestId OR RequestId IN (SELECT RequestId FROM {OwnedSql.Table<IapPurchase>()} WHERE PurchaseToken = @PurchaseToken AND CreateUserId = @CreateUserId)",
            rows => state.Receipts = rows, activeOnly: false));
        // 購買紀錄不依擁有者過濾：token 被其他帳號使用過時必須能偵測。
        reads.Add(new SnapshotRead<object?>(
            $"SELECT TOP 1 * FROM {OwnedSql.Table<IapPurchase>()} WHERE PurchaseToken = @PurchaseToken AND Status != -1;",
            async reader => { state.Token = (await reader.ReadAsync<IapPurchase>()).FirstOrDefault(); return null; }));
        reads.Add(OwnedSql.Rows<Item>($"Amount > 0 OR ItemKey = '{PurchaseCatalog.DiamondItemKey}'", rows => state.Items = rows));

        string payload = RevisionCommand.CanonicalPayload(new { request.ProductId, request.PurchaseToken });
        await using (var reader = await _repository.QueryMultipleAsync(reads.Sql,
            new { CreateUserId = _playerId, request.RequestId, request.PurchaseToken }))
        {
            await reads.ReadAsync(reader);
        }

        EconomyRequest? prior = state.Receipts.FirstOrDefault(r => r.RequestId == request.RequestId);
        if (prior != null)
        {
            if (prior.Operation != Operation || prior.Payload != payload)
            { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
            return await Replay(prior, state.Token, request.RequestId);
        }
        if (state.Token != null)
        {
            // 他人使用過的 token 一律拒絕；自己以新 request_id 重送同一 token 則重播原回條（不再發放）。
            if (state.Token.CreateUserId != _playerId) { throw new BusinessOperationException(ApiMessageKeys.PurchaseTokenConflict); }
            EconomyRequest? original = state.Receipts.FirstOrDefault(r => r.RequestId == state.Token.RequestId);
            if (original == null || original.Operation != Operation || original.Payload != payload)
            { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
            return await Replay(original, state.Token, request.RequestId);
        }

        string orderId = await VerifyWithGoogle(request);
        SqlBatchCommand write = PrepareWrite(_playerId, request, payload, state, diamonds, orderId, out Guid purchaseId);
        GooglePlayPurchaseReceipt receipt;
        try
        {
            string json = await _repository.QueryFirstAsync<string>(write.Sql, write.Parameters);
            receipt = JsonSerializer.Deserialize<GooglePlayPurchaseReceipt>(json)
                ?? throw new InvalidOperationException("Invalid purchase receipt.");
        }
        catch (System.Data.SqlClient.SqlException error)
            when (error.Number is UniqueIndexViolation or UniqueConstraintViolation or RewardRowError)
        {
            // 並行的同 token／同請求已先提交：交由用戶端以原請求重送，走回條重播。
            throw new BusinessOperationException(ApiMessageKeys.EconomyRequestConflict);
        }
        receipt.Consumed = await TryConsume(request.ProductId, request.PurchaseToken, purchaseId);
        return receipt;
    }

    /// <summary>向 Google 確認這筆購買真實、已付款且尚未 consume；回傳訂單編號。</summary>
    private async Task<string> VerifyWithGoogle(GooglePlayPurchaseRequest request)
    {
        GooglePlayProductPurchase? purchase;
        try
        {
            purchase = await billing.GetProductPurchaseAsync(request.ProductId, request.PurchaseToken, CancellationToken.None);
        }
        catch (GooglePlayUnavailableException error)
        {
            logger.LogError(error, "Google Play purchase lookup failed for {ProductId}.", request.ProductId);
            throw new BusinessOperationException(ApiMessageKeys.PurchaseUnavailable);
        }
        if (purchase == null) { throw new BusinessOperationException(ApiMessageKeys.PurchaseTokenInvalid); }
        if (purchase.PurchaseState != PurchasedState || purchase.ConsumptionState == ConsumedState)
        {
            throw new BusinessOperationException(ApiMessageKeys.PurchaseNotCompleted);
        }
        return purchase.OrderId;
    }

    private async Task<GooglePlayPurchaseReceipt> Replay(EconomyRequest prior, IapPurchase? token, Guid requestId)
    {
        GooglePlayPurchaseReceipt receipt = JsonSerializer.Deserialize<GooglePlayPurchaseReceipt>(prior.Result)
            ?? throw new InvalidOperationException("Invalid purchase receipt.");
        receipt.RequestId = requestId;
        if (token == null) { throw new InvalidOperationException("Purchase receipt has no purchase record."); }
        receipt.Consumed = token.Consumed || await TryConsume(token.ProductId, token.PurchaseToken, token.Id);
        return receipt;
    }

    /// <summary>consume 失敗不影響已發放的鑽石；保留 Consumed=false 讓原請求重送時重試。</summary>
    private async Task<bool> TryConsume(string productId, string purchaseToken, Guid purchaseId)
    {
        try
        {
            await billing.ConsumeAsync(productId, purchaseToken, CancellationToken.None);
        }
        catch (GooglePlayUnavailableException error)
        {
            logger.LogError(error, "Google Play consume failed for {ProductId}; will retry on replay.", productId);
            return false;
        }
        await _repository.ExecuteAsync(
            $"UPDATE {OwnedSql.Table<IapPurchase>()} SET Consumed = 1 WHERE Id = @Id AND CreateUserId = @CreateUserId;",
            new { Id = purchaseId, CreateUserId = _playerId });
        return true;
    }

    internal static SqlBatchCommand PrepareWrite(Guid? playerId, GooglePlayPurchaseRequest request, string payload,
        State state, int diamonds, string orderId, out Guid purchaseId)
    {
        var writes = new SqlWriteBatch(playerId);
        DateTime now = DateTime.Now;
        writes.Add(BeginTransaction);

        var purchase = new IapPurchase
        {
            PurchaseToken = request.PurchaseToken, ProductId = request.ProductId, OrderId = orderId,
            RequestId = request.RequestId, Diamonds = diamonds, Consumed = false,
        };
        purchaseId = purchase.Id;
        writes.Insert([purchase]);

        // 與成就領獎相同語意：只認未刪除的列；沒有就新增，既有列以 SQL 相對加上避免覆蓋並行的鑽石同步。
        Item? reward = state.Items.FirstOrDefault(i => i.ItemKey == PurchaseCatalog.DiamondItemKey);
        bool rewardExists = reward != null;
        if (reward == null)
        {
            reward = new Item { ItemKey = PurchaseCatalog.DiamondItemKey, Amount = diamonds, Category = "" };
            state.Items.Add(reward);
            writes.Insert([reward]);
        }
        else
        {
            reward.Amount = checked(reward.Amount + diamonds);
        }

        List<Item> inStock = state.Items.Where(i => i.Amount > 0).ToList();
        var receipt = new GooglePlayPurchaseReceipt
        {
            RequestId = request.RequestId, ProductId = request.ProductId, Diamonds = diamonds, Items = inStock,
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
            Reward = diamonds, ItemId = reward.Id, Result = JsonSerializer.Serialize(receipt), Now = now,
            Enabled = (int)Library.Models.Enum.EStatus.啟用,
            request.RequestId, Operation, Payload = payload,
        });
        IReadOnlyList<SqlBatchCommand> commands = writes.Commands;
        if (commands.Count != 1) { throw new InvalidOperationException("Purchase write must be one atomic batch."); }
        return commands[0];
    }
}
