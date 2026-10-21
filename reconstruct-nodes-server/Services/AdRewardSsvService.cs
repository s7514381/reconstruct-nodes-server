using Dapper;
using ReconstructNodesServer.Billing;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// AdMob 伺服器端驗證（SSV）回呼：驗證 Google 簽章後，為 user_id 玩家建立「已驗證、未領取」的 <see cref="AdRewardGrant"/>。
/// 此處不發放鑽石，也不看回呼帶的 reward_amount：數量、每日上限與發放都在玩家領取時由 <see cref="AdRewardCommand"/> 決定。
/// transaction_id 唯一，Google 重送同一回呼只會留下一筆。
/// </summary>
public class AdRewardSsvService(IDapperRepository repository, IAdMobSsvKeySource keys, AdMobSsvOptions options,
    TimeProvider clock, ILogger<AdRewardSsvService> logger)
{
    public enum Outcome
    {
        /// <summary>簽章有效（含重送與無法對應玩家的測試回呼）：回 200。</summary>
        Accepted,
        /// <summary>簽章或內容無效：回 400，AdMob 不應再重送。</summary>
        Rejected,
        /// <summary>暫時無法驗證（金鑰、資料庫）：回 503，AdMob 會重試。</summary>
        Unavailable,
    }

    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;
    private const int MaxTransactionIdLength = 450;

    public async Task<Outcome> Handle(string rawQuery, IQueryCollection query, CancellationToken cancellationToken)
    {
        AdMobSsvSignature.Signed? signed = AdMobSsvSignature.Parse(rawQuery);
        if (signed == null) { return Outcome.Rejected; }
        try
        {
            byte[]? key = await keys.GetPublicKeyAsync(signed.KeyId, cancellationToken);
            if (key == null || !AdMobSsvSignature.Verify(key, signed)) { return Outcome.Rejected; }
        }
        catch (AdMobKeysUnavailableException error)
        {
            logger.LogError(error, "AdMob SSV keys unavailable.");
            return Outcome.Unavailable;
        }

        if (options.AdUnitId.Length > 0 && query["ad_unit"] != options.AdUnitId) { return Outcome.Rejected; }
        string transactionId = query["transaction_id"].ToString();
        if (transactionId.Length == 0 || transactionId.Length > MaxTransactionIdLength) { return Outcome.Rejected; }
        // 簽章有效但 user_id／custom_data 不是遊戲帶入的 GUID（例如 AdMob 後台的測試回呼）：確認收到，不建立紀錄。
        if (!Guid.TryParse(query["user_id"], out Guid playerId) || !Guid.TryParse(query["custom_data"], out Guid requestId))
        {
            logger.LogInformation("AdMob SSV callback without player context ignored.");
            return Outcome.Accepted;
        }

        try
        {
            int inserted = await repository.ExecuteAsync(InsertSql, new
            {
                Now = clock.GetLocalNow().DateTime, UserId = playerId, TransactionId = transactionId, RequestId = requestId,
                Enabled = (int)Library.Models.Enum.EStatus.啟用, Amount = AdRewardCatalog.DiamondsPerAd,
            });
            if (inserted == 0) { logger.LogInformation("AdMob SSV callback is a duplicate or has no player."); }
            return Outcome.Accepted;
        }
        catch (System.Data.SqlClient.SqlException error) when (error.Number is UniqueIndexViolation or UniqueConstraintViolation)
        {
            // 並行重送或同一 request_id 已有紀錄：已有驗證列，視為已收到。
            return Outcome.Accepted;
        }
        catch (System.Data.SqlClient.SqlException error)
        {
            logger.LogError(error, "AdMob SSV grant could not be stored.");
            return Outcome.Unavailable;
        }
    }

    private static readonly string InsertSql = $"""
        INSERT INTO {OwnedSql.Table<AdRewardGrant>()} (Id, CreateDate, CreateUserId, Status, TransactionId, RequestId, Claimed, Amount)
        SELECT NEWID(), @Now, @UserId, @Enabled, @TransactionId, @RequestId, 0, @Amount
        WHERE EXISTS (SELECT 1 FROM {OwnedSql.Table<Player>()} WHERE Id = @UserId)
          AND NOT EXISTS (SELECT 1 FROM {OwnedSql.Table<AdRewardGrant>()} WHERE TransactionId = @TransactionId AND Status != -1);
        """;
}
