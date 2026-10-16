using System.Globalization;
using System.Text.Json;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services;

/// <summary>
/// 領取成就獎勵：比照 Shop/Draw 的單批次寫入，總共兩次 SQL 往返且不依賴背景排程。
/// 第一次往返讀出成就資料列、鑽石與回條，在記憶體驗證並推導；第二次往返由 T-SQL 自行
/// BEGIN TRAN、以鎖定讀取檢查 revision 柵欄、只寫入被領取的成就群組／項目、鑽石（相對加一）、
/// revision 與回條，再 COMMIT。不使用快照隔離，因此與同一玩家的鑽石同步並行時不會因更新衝突重做整筆交易。
/// 回執同時帶回領取後的在庫物品，取代領取後再讀 Item 清單。
/// </summary>
public class AchievementClaimCommand(BaseServiceArgument argument) : BaseService(argument)
{
    private const string Operation = "achievement.claim";
    private const string RevisionName = nameof(AchievementProgressModel);
    // 與用戶端 AchievementItem.REWARD_ID、REWARD_AMOUNT 保持一致
    private const string RewardItemKey = "diamond";
    private const int RewardAmount = 1;
    // THROW 編號：revision 柵欄不符（訊息帶目前 revision）、鑽石列在讀寫之間消失。
    private const int FenceError = 51010;
    private const int RewardRowError = 51011;

    private const string BeginAndFence = """
        SET XACT_ABORT ON;
        BEGIN TRAN;
        DECLARE @Current nvarchar(50) = (SELECT TOP 1 Value FROM PlayerParams WITH (UPDLOCK, HOLDLOCK)
            WHERE CreateUserId = @CreateUserId AND [Group] = @Group AND Name = @Name AND Status != -1);
        SET @Current = ISNULL(@Current, N'0');
        IF @Current <> @Expected THROW 51010, @Current, 1;
        """;

    private const string PersistReceipt = """
        INSERT INTO EconomyRequest (Id, CreateDate, CreateUserId, Status, RequestId, Operation, Payload, Result)
        VALUES (NEWID(), @Now, @CreateUserId, @Enabled, @RequestId, @Operation, @Payload, @Result);
        COMMIT;
        SELECT @Result;
        """;

    private sealed class State
    {
        public List<EconomyRequest> Receipts { get; set; } = [];
        public AchievementProgressCommand.AchievementRows Rows { get; set; } = null!;
        public List<Item> Items { get; set; } = [];
    }

    public async Task<CommandReceipt<AchievementClaimSnapshot>> Execute(AchievementClaimRequest request)
    {
        if (request.RequestId == Guid.Empty) { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestRequired); }
        if (string.IsNullOrWhiteSpace(request.AchievementItemId)) { throw new BusinessArgumentException(ApiMessageKeys.AchievementIdRequired); }
        var state = new State();
        var reads = new SnapshotReadBatch();
        reads.Add(OwnedSql.OwnerRead("AchievementClaim"));
        reads.Add(OwnedSql.Rows<EconomyRequest>("RequestId = @RequestId", rows => state.Receipts = rows, activeOnly: false));
        var rowsRead = AchievementProgressCommand.RowsRead;
        reads.Add(new SnapshotRead<object?>(rowsRead.Sql, async reader => { state.Rows = await rowsRead.Read(reader); return null; }));
        // 在庫清單，外加獎勵物品的未刪除列（含數量 0 的殘列，與 ItemService.AdjustAmount 找到的列一致）。
        reads.Add(OwnedSql.Rows<Item>($"Amount > 0 OR ItemKey = '{RewardItemKey}'", rows => state.Items = rows));

        string payload = RevisionCommand.CanonicalPayload(request);
        SqlBatchCommand write;
        await using (var reader = await _repository.QueryMultipleAsync(reads.Sql, new { CreateUserId = _playerId, request.RequestId }))
        {
            await reads.ReadAsync(reader);
            write = Prepare(request, payload, state);
            if (!reader.IsConsumed) { throw new InvalidOperationException("Claim read batch must consume every result."); }
        }
        try
        {
            string json = await _repository.QueryFirstAsync<string>(write.Sql, write.Parameters);
            return JsonSerializer.Deserialize<CommandReceipt<AchievementClaimSnapshot>>(json)
                ?? throw new InvalidOperationException("Invalid command receipt.");
        }
        catch (System.Data.SqlClient.SqlException error) when (error.Number is FenceError or RewardRowError)
        {
            throw RevisionConflict(error.Number == FenceError ? error.Message : "0");
        }
    }

    private static BusinessOperationException RevisionConflict(string revision) =>
        new(new ApiMessage("concurrency.revision_conflict",
            new Dictionary<string, string> { ["domain"] = RevisionName, ["revision"] = revision }));

    private SqlBatchCommand Prepare(AchievementClaimRequest request, string payload, State state)
    {
        var prior = state.Receipts.FirstOrDefault(r => r.RequestId == request.RequestId);
        if (prior != null)
        {
            if (prior.Operation != Operation || prior.Payload != payload)
            { throw new BusinessArgumentException(ApiMessageKeys.EconomyRequestConflict); }
            return new SqlBatchCommand("SELECT @Receipt;", new { Receipt = prior.Result });
        }

        var rows = state.Rows;
        long current = rows.Revision;
        // 成就領域沒有實例範圍，fence 等於目前 revision；過期或超前的期望版本一律視為衝突。
        if (request.ExpectedRevision != current)
        {
            throw RevisionConflict(current.ToString(CultureInfo.InvariantCulture));
        }

        AchievementProgressModel progress = AchievementProgressCommand.Derive(rows);
        AchievementGroupModel? group = progress.Groups.FirstOrDefault(g => g.Items.Any(i => i.Name == request.AchievementItemId));
        AchievementItemModel target = group?.Items.First(i => i.Name == request.AchievementItemId)
            ?? throw new BusinessOperationException(ApiMessageKeys.AchievementItemMissing);
        if (target.IsReward) { throw new BusinessOperationException(ApiMessageKeys.AchievementClaimed); }
        if (group!.Total < target.Target) { throw new BusinessOperationException(ApiMessageKeys.AchievementIncomplete); }

        var writes = new SqlWriteBatch(_playerId);
        DateTime now = DateTime.Now;
        // 交易與 revision 柵欄先行：過期請求在取得鎖後立即失敗，並行的重複領取只會有一個通過。
        writes.Add(BeginAndFence, new
        {
            Group = RevisionCommand.RevisionGroup, Name = RevisionName,
            Expected = current.ToString(CultureInfo.InvariantCulture),
        });

        // 只落地被領取的群組：推導進度、尚未保存的收集項目，以及領取標記。
        Achievement? stored = rows.Groups.FirstOrDefault(g => g.Name == group.Name);
        if (stored == null)
        {
            stored = new Achievement { Name = group.Name, Total = group.Total, Sequence = progress.Groups.IndexOf(group) };
            writes.Insert([stored]);
        }
        else if (stored.Total != group.Total)
        {
            stored.Total = group.Total;
            writes.Update([stored]);
        }
        var storedCollected = rows.Collected.Where(c => c.AchievementId == stored.Id).Select(c => c.Name).ToHashSet();
        writes.Insert(group.Collected.Where(name => !storedCollected.Contains(name)).Select((name, index) =>
            new AchievementCollected { AchievementId = stored.Id, Name = name, Sequence = storedCollected.Count + index }));

        AchievementDetail? detail = rows.Details.FirstOrDefault(d => d.AchievementId == stored.Id && d.Name == target.Name);
        if (detail == null)
        {
            writes.Insert([new AchievementDetail
            {
                AchievementId = stored.Id, Name = target.Name, Target = target.Target, IsReward = true,
                RewardDate = now, Sequence = group.Items.IndexOf(target),
            }]);
        }
        else
        {
            detail.IsReward = true;
            detail.RewardDate = now;
            writes.Update([detail]);
        }

        // 與 ItemService.AdjustAmount 相同語意：只認未刪除的列；沒有就新增，數量只會增加。
        // 既有列以 SQL 相對加一，並行的鑽石同步不會互相覆蓋；回執中的鑽石數量由 SQL 回填實際值。
        Item? reward = state.Items.FirstOrDefault(i => i.ItemKey == RewardItemKey);
        bool rewardExists = reward != null;
        if (reward == null)
        {
            reward = new Item { ItemKey = RewardItemKey, Amount = RewardAmount, Category = "" };
            state.Items.Add(reward);
            writes.Insert([reward]);
        }
        else
        {
            reward.Amount = checked(reward.Amount + RewardAmount);
        }

        // 領取一律推進 revision；柵欄已鎖定該列，這裡直接寫入新值。
        long next = checked(current + 1);
        if (rows.RevisionRow == null)
        {
            writes.Insert([new PlayerParams { Group = RevisionCommand.RevisionGroup, Name = RevisionName,
                Value = next.ToString(CultureInfo.InvariantCulture) }]);
        }
        else
        {
            rows.RevisionRow.Value = next.ToString(CultureInfo.InvariantCulture);
            writes.Update([rows.RevisionRow]);
        }

        target.IsReward = true;
        target.RewardDate = now;
        progress.Revision = next;
        List<Item> inStock = state.Items.Where(i => i.Amount > 0).ToList();
        var receipt = new CommandReceipt<AchievementClaimSnapshot>
        {
            RequestId = request.RequestId, Revision = next,
            Snapshot = new AchievementClaimSnapshot { Progress = progress, Items = inStock },
        };
        string resultJson = JsonSerializer.Serialize(receipt);
        string fragment = PersistReceipt;
        if (rewardExists)
        {
            int index = inStock.IndexOf(reward);
            fragment = $"""
                DECLARE @Amount int;
                UPDATE Item SET @Amount = Amount = Amount + @Reward
                WHERE Id = @ItemId AND CreateUserId = @CreateUserId AND Status != -1;
                IF @@ROWCOUNT <> 1 THROW 51011, N'0', 1;
                SET @Result = JSON_MODIFY(@Result, '$.snapshot.items[{index}].Amount', @Amount);
                {PersistReceipt}
                """;
        }
        writes.Add(fragment, new
        {
            Reward = RewardAmount, ItemId = reward.Id, Result = resultJson, Now = now,
            Enabled = (int)Library.Models.Enum.EStatus.啟用,
            request.RequestId, Operation, Payload = payload,
        });
        IReadOnlyList<SqlBatchCommand> commands = writes.Commands;
        if (commands.Count != 1) { throw new InvalidOperationException("Claim write must be one atomic batch."); }
        return commands[0];
    }
}
