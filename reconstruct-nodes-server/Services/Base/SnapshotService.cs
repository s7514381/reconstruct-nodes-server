using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Utility;
using System.Linq.Expressions;
using System.Reflection;
using Dapper;
using ReconstructNodesServer.Repositories;

namespace ReconstructNodesServer.Services
{
    public abstract class _SnapshotService<DBModel, Snapshot>(BaseServiceArgument argument)
        : _ModelService<DBModel, DBModel>(argument)
        where DBModel : ModelBase, new()
        where Snapshot : class
    {
        protected RevisionCommand Revisions { get; } = new(argument);

        public virtual async Task Save(Snapshot model)
        {
            ValidateSnapshot(model);
            string domain = typeof(Snapshot).Name;
            if (model is IRevisionedSnapshot versioned)
            {
                await Revisions.Check(domain, versioned.ExpectedRevision, RevisionCommand.ScopesFor(versioned));
            }
            await SaveSnapshot(model);
            if (model is IRevisionedSnapshot saved && saved.ExpectedRevision.HasValue)
            {
                saved.Revision = await Revisions.Advance(domain, RevisionCommand.ScopesFor(saved));
            }
        }

        protected abstract void ValidateSnapshot(Snapshot model);
        protected abstract Task SaveSnapshot(Snapshot model);

        /// <summary>限定欄位更新；不更動自然鍵、狀態與建立資訊。</summary>
        protected Task<int> UpdateSnapshotFieldsAsync(DBModel row, params Expression<Func<DBModel, object?>>[] fields)
        {
            string[] names = fields.Select(Field).Distinct().ToArray();
            if (names.Length == 0 || names.Any(name => name is nameof(ModelBase.Id) or nameof(ModelBase.CreateUserId)
                or nameof(ModelBase.CreateDate) or nameof(ModelBase.Status) || name == NaturalKeyField))
            {
                throw new ArgumentException("限定更新不可包含識別、建立、狀態或自然鍵欄位", nameof(fields));
            }
            DynamicParameters parameters = new();
            parameters.Add("Id", row.Id);
            parameters.Add("CreateUserId", _playerId);
            foreach (string name in names) { parameters.Add(name, typeof(DBModel).GetProperty(name)!.GetValue(row)); }
            return ExecuteAsync($"UPDATE {_tableName} SET {string.Join(", ", names.Select(name => $"{name} = @{name}"))} WHERE Id = @Id AND CreateUserId = @CreateUserId", parameters);
        }

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
