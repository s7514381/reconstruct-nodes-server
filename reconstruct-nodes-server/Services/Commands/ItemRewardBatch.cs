using System.Text.Json;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 伺服器發放單一物品獎勵的原子批次尾段（每日簽到、挑戰通關共用）：
/// 物品只認未刪除的列，沒有就新增、既有列以 SQL 相對加上避免覆蓋並行的物品同步，
/// 最後寫入回條並 COMMIT。呼叫端先加入 <see cref="BeginTransaction"/> 與自己的紀錄寫入，再呼叫 <see cref="Complete{TReceipt}"/>。
/// </summary>
internal static class ItemRewardBatch
{
    /// <summary>物品列在讀取後被並行修改或刪除：呼叫端轉成請求衝突，由用戶端重送。</summary>
    public const int RewardRowError = 51031;
    public const string BeginTransaction = "SET XACT_ABORT ON; BEGIN TRAN;";

    private const string PersistReceipt = """
        INSERT INTO EconomyRequest (Id, CreateDate, CreateUserId, Status, RequestId, Operation, Payload, Result)
        VALUES (NEWID(), @Now, @CreateUserId, @Enabled, @RequestId, @Operation, @Payload, @Result);
        COMMIT;
        SELECT @Result;
        """;

    /// <param name="items">讀回的物品列；發放後同步更新記憶體內數量，回條據此帶回在庫物品。</param>
    /// <param name="buildReceipt">以發放後在庫物品（數量 &gt; 0）組出回條。</param>
    public static SqlBatchCommand Complete<TReceipt>(SqlWriteBatch writes, List<Item> items, string itemKey, int amount,
        Func<List<Item>, TReceipt> buildReceipt, DateTime now, Guid requestId, string operation, string payload)
    {
        Item? reward = items.FirstOrDefault(i => i.ItemKey == itemKey);
        bool rewardExists = reward != null;
        if (reward == null)
        {
            reward = new Item { ItemKey = itemKey, Amount = amount, Category = "" };
            items.Add(reward);
            writes.Insert([reward]);
        }
        else
        {
            reward.Amount = checked(reward.Amount + amount);
        }

        List<Item> inStock = items.Where(i => i.Amount > 0).ToList();
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
            Reward = amount, ItemId = reward.Id, Result = JsonSerializer.Serialize(buildReceipt(inStock)), Now = now,
            Enabled = (int)Library.Models.Enum.EStatus.啟用,
            RequestId = requestId, Operation = operation, Payload = payload,
        });
        IReadOnlyList<SqlBatchCommand> commands = writes.Commands;
        if (commands.Count != 1) { throw new InvalidOperationException("Reward write must be one atomic batch."); }
        return commands[0];
    }
}
