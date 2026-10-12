using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Services
{
    public abstract class _SnapshotService<DBModel, Snapshot>(BaseServiceArgument argument)
        : _ModelService<DBModel, DBModel>(argument)
        where DBModel : ModelBase, new()
        where Snapshot : class
    {
        public virtual async Task Save(Snapshot model)
        {
            ValidateSnapshot(model);
            await SaveSnapshot(model);
        }

        protected abstract void ValidateSnapshot(Snapshot model);
        protected abstract Task SaveSnapshot(Snapshot model);

        /// <summary>
        /// 在呼叫端交易內鎖定本人父列並 upsert；自然鍵欄位由 Service 指定。
        /// </summary>
        protected async Task<(DBModel Row, bool IsNew)> UpsertSnapshotParentAsync(
            Action<DBModel> map, string? naturalKey = null, Func<DBModel, Task>? update = null)
        {
            string keyFilter = "";
            if (naturalKey != null)
            {
                if (string.IsNullOrEmpty(NaturalKeyField))
                {
                    throw new InvalidOperationException($"{GetType().Name} 未設定 NaturalKeyField");
                }
                keyFilter = $" AND {NaturalKeyField} = @NaturalKey";
            }

            string sql = $@"SELECT TOP 1 *
                              FROM {_tableName} WITH (UPDLOCK, HOLDLOCK)
                             WHERE Status != -1
                               AND CreateUserId = @CreateUserId{keyFilter}";
            DBModel? existing = await QueryFirstAsync<DBModel?>(sql, new { CreateUserId = _playerId, NaturalKey = naturalKey });
            DBModel row = existing ?? new DBModel { CreateUserId = _playerId };
            map(row);

            if (existing == null)
            {
                await InsertAsync(row);
            }
            else if (update != null)
            {
                await update(row);
            }
            else
            {
                await UpdateAsync(row);
            }

            return (row, existing == null);
        }
    }
}
