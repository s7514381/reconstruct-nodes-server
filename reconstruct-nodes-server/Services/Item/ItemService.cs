using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class ItemService(BaseServiceArgument argument) : _ModelService<Item, Item>(argument)
    {
        internal const string WhitespaceCharacters =
            "\u0009\u000a\u000b\u000c\u000d\u0020\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000";
        protected override string? NaturalKeyField => "ItemKey";

        private string InStockSql => $@"SELECT *
                              FROM {_tableName}
                             WHERE Status != -1
                               AND CreateUserId = @CreateUserId
                               AND Amount > 0";

        public virtual async Task<IEnumerable<Item>> GetInStockList() =>
            await QueryAsync<Item>(InStockSql, new { CreateUserId = _playerId });

        public SnapshotRead<List<Item>> InStockListRead =>
            new(InStockSql + ";", async reader => (await reader.ReadAsync<Item>()).ToList());

        /// <summary>
        /// 以增量調整物品數量並回傳結果數量，需在交易內呼叫。
        /// 數量歸零時軟刪除該列；結果小於 0 時拋出例外，由交易回滾。
        /// 分類僅在建立列或既有列分類為空白時寫入。
        /// </summary>
        public virtual async Task<int> AdjustAmount(string itemKey, int delta, string? category = null)
        {
            Item created = new() { ItemKey = itemKey, CreateUserId = _playerId };
            try
            {
                return await QueryFirstAsync<int>(AdjustmentSql + " SELECT @Amount;", new
                {
                    CreateUserId = _playerId, ItemKey = itemKey, Delta = delta, Category = category,
                    NewId = created.Id, created.CreateDate, Status = (int?)created.Status,
                    Whitespace = WhitespaceCharacters,
                });
            }
            catch (System.Data.SqlClient.SqlException error) when (error.Number == 51001)
            {
                throw new BusinessOperationException(ApiMessageKeys.ItemInsufficient);
            }
            catch (System.Data.SqlClient.SqlException error) when (error.Number == 8115)
            {
                throw new OverflowException("Item amount overflowed.", error);
            }
        }

        /// <summary>
        /// 一次套用多筆物品增量（參數 @Items 為 [{k,d,c}] JSON），語意同 <see cref="AdjustmentSql"/>：
        /// 同 key 先加總；不足拋 51001，擁有者不存在拋 51002，數量溢位為 8115。
        /// 只用固定數量的陳述式，取代每個物品各一段 SQL。
        /// </summary>
        internal const string BulkAdjustmentSql = """
                        IF NOT EXISTS (SELECT 1 FROM Player WHERE Id = @CreateUserId) THROW 51002, 'Item owner is unavailable', 1;
                        DECLARE @Changes TABLE (ItemKey nvarchar(max), Delta int, Category nvarchar(max),
                            ExistingId uniqueidentifier, Amount int, ExistingCategory nvarchar(max));
                        WITH j AS (SELECT CAST([key] AS int) AS Ord, JSON_VALUE(value, '$.k') AS ItemKey,
                                CAST(JSON_VALUE(value, '$.d') AS int) AS Delta, JSON_VALUE(value, '$.c') AS Category
                            FROM OPENJSON(@Items))
                        INSERT INTO @Changes
                        SELECT g.ItemKey, g.Delta, g.Category, e.Id, e.Amount, e.Category
                        FROM (SELECT j.ItemKey, SUM(j.Delta) AS Delta,
                                  (SELECT TOP 1 j2.Category FROM j j2 WHERE j2.ItemKey = j.ItemKey AND j2.Category IS NOT NULL
                                   ORDER BY j2.Ord DESC) AS Category
                              FROM j GROUP BY j.ItemKey) g
                        OUTER APPLY (SELECT TOP 1 Id, Amount, Category FROM Item WITH (UPDLOCK, HOLDLOCK)
                                     WHERE Status != -1 AND CreateUserId = @CreateUserId AND ItemKey = g.ItemKey) e;
                        IF EXISTS (SELECT 1 FROM @Changes WHERE (ExistingId IS NULL AND Delta <= 0) OR (ExistingId IS NOT NULL AND Amount + Delta < 0))
                            THROW 51001, 'Insufficient item amount', 1;
                        INSERT INTO Item (Id, CreateDate, CreateUserId, Status, ItemKey, Amount, Category)
                        SELECT NEWID(), @CreateDate, @CreateUserId, @Status, ItemKey, Delta, COALESCE(Category, N'')
                        FROM @Changes WHERE ExistingId IS NULL;
                        UPDATE i SET Amount = c.Amount + c.Delta,
                            Status = CASE WHEN c.Amount + c.Delta = 0 THEN -1 ELSE i.Status END,
                            Category = CASE WHEN c.Amount + c.Delta = 0 THEN i.Category
                                WHEN c.ExistingCategory IS NULL OR DATALENGTH(TRIM(@Whitespace FROM c.ExistingCategory)) = 0
                                    THEN COALESCE(c.Category, N'') ELSE i.Category END
                        FROM Item i JOIN @Changes c ON c.ExistingId = i.Id
                        WHERE i.CreateUserId = @CreateUserId;
                    """;

        internal const string AdjustmentSql = """
                        DECLARE @Player uniqueidentifier;
                        SELECT @Player = Id FROM Player WHERE Id = @CreateUserId;
                        IF @Player IS NULL THROW 51002, 'Item owner is unavailable', 1;
                        DECLARE @Id uniqueidentifier, @Amount int, @ExistingCategory nvarchar(max);
                        SELECT TOP 1 @Id = Id, @Amount = Amount, @ExistingCategory = Category
                        FROM Item WITH (UPDLOCK, HOLDLOCK)
                        WHERE Status != -1 AND CreateUserId = @CreateUserId AND ItemKey = @ItemKey;
                        IF @Id IS NULL
                        BEGIN
                            IF @Delta <= 0 THROW 51001, 'Insufficient item amount', 1;
                            SET @Amount = @Delta;
                            INSERT INTO Item (Id, CreateDate, CreateUserId, Status, ItemKey, Amount, Category)
                            VALUES (@NewId, @CreateDate, @CreateUserId, @Status, @ItemKey, @Amount, COALESCE(@Category, N''));
                        END
                        ELSE
                        BEGIN
                            SET @Amount = @Amount + @Delta;
                            IF @Amount < 0 THROW 51001, 'Insufficient item amount', 1;
                            IF @Amount = 0
                                UPDATE Item SET Amount = 0, Status = -1 WHERE Id = @Id AND CreateUserId = @CreateUserId;
                            ELSE
                                UPDATE Item SET Amount = @Amount,
                                    Category = CASE WHEN @ExistingCategory IS NULL OR
                                        DATALENGTH(TRIM(@Whitespace FROM @ExistingCategory)) = 0
                                        THEN COALESCE(@Category, N'') ELSE Category END
                                WHERE Id = @Id AND CreateUserId = @CreateUserId;
                        END;
                    """;
    }
}
