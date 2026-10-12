using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class ItemService(BaseServiceArgument argument) : _ModelService<Item, Item>(argument)
    {
        protected override string? NaturalKeyField => "ItemKey";

        public virtual async Task<IEnumerable<Item>> GetInStockList()
        {
            string sql = $@"SELECT *
                              FROM {_tableName}
                             WHERE Status != -1
                               AND CreateUserId = @CreateUserId
                               AND Amount > 0";
            return await QueryAsync<Item>(sql, new { CreateUserId = _playerId });
        }

        /// <summary>
        /// 以增量調整物品數量並回傳結果數量，需在交易內呼叫。
        /// 數量歸零時軟刪除該列；結果小於 0 時拋出例外，由交易回滾。
        /// 名稱與分類僅在建立列或既有列名稱為空白時寫入。
        /// </summary>
        public virtual async Task<int> AdjustAmount(string itemKey, int delta, string? name = null, string? category = null)
        {
            // UPDLOCK + HOLDLOCK 同時鎖住既有列與不存在時的鍵區間，避免並行增量遺失更新或重複插入
            string sql = $@"SELECT TOP 1 *
                              FROM {_tableName} WITH (UPDLOCK, HOLDLOCK)
                             WHERE Status != -1
                               AND CreateUserId = @CreateUserId
                               AND ItemKey = @ItemKey";
            Item? item = await QueryFirstAsync<Item?>(sql, new { CreateUserId = _playerId, ItemKey = itemKey });

            if (item == null)
            {
                if (delta <= 0) { throw new InvalidOperationException($"物品 {itemKey} 數量不足，無法扣除"); }

                Item newItem = new Item
                {
                    ItemKey = itemKey,
                    Amount = delta,
                    Name = name ?? string.Empty,
                    Category = category ?? string.Empty,
                    CreateUserId = _playerId,
                };
                await InsertAsync(newItem);
                return delta;
            }

            int amount = item.Amount + delta;
            if (amount < 0) { throw new InvalidOperationException($"物品 {itemKey} 數量不足（現有 {item.Amount}，扣除 {-delta}）"); }

            if (amount == 0)
            {
                string deleteSql = $@"UPDATE {_tableName}
                                         SET Amount = 0,
                                             Status = -1
                                       WHERE Id = @Id
                                         AND CreateUserId = @CreateUserId";
                await ExecuteAsync(deleteSql, new { Id = item.Id, CreateUserId = _playerId });
            }
            else
            {
                string updateSql = $@"UPDATE {_tableName}
                                         SET Amount = @Amount,
                                             Name = @Name,
                                             Category = @Category
                                       WHERE Id = @Id
                                         AND CreateUserId = @CreateUserId";
                await ExecuteAsync(updateSql, new
                {
                    Id = item.Id,
                    CreateUserId = _playerId,
                    Amount = amount,
                    Name = string.IsNullOrWhiteSpace(item.Name) ? (name ?? string.Empty) : item.Name,
                    Category = string.IsNullOrWhiteSpace(item.Category) ? (category ?? string.Empty) : item.Category,
                });
            }

            return amount;
        }
    }
}
