using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Utility;
using System.Linq.Expressions;
using System.Reflection;
using Dapper;

namespace ReconstructNodesServer.Services
{
    public abstract class _SnapshotService<DBModel, Snapshot>(BaseServiceArgument argument)
        : _ModelService<DBModel, DBModel>(argument)
        where DBModel : ModelBase, new()
        where Snapshot : class
    {
        protected RevisionService Revisions { get; } = new(argument);

        public virtual async Task Save(Snapshot model)
        {
            ValidateSnapshot(model);
            string domain = typeof(Snapshot).Name;
            if (model is IRevisionedSnapshot versioned)
            {
                await Revisions.Check(domain, versioned.ExpectedRevision, RevisionService.ScopesFor(versioned));
            }
            await SaveSnapshot(model);
            if (model is IRevisionedSnapshot saved && saved.ExpectedRevision.HasValue)
            {
                saved.Revision = await Revisions.Advance(domain, RevisionService.ScopesFor(saved));
            }
        }

        protected abstract void ValidateSnapshot(Snapshot model);
        protected abstract Task SaveSnapshot(Snapshot model);

        // 僅接受直接屬性，識別字一律來自模型，不接受外部 SQL。
        private static string Field<T, TValue>(Expression<Func<T, TValue>> selector)
        {
            Expression body = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert
                ? convert.Operand : selector.Body;
            if (body is not MemberExpression { Member: PropertyInfo property } member ||
                member.Expression != selector.Parameters[0])
            {
                throw new ArgumentException("必須選取模型的直接屬性", nameof(selector));
            }
            return property.Name;
        }

        private static string Table<T>() where T : ModelBase
        {
            string table = CommonUtility.GetTableAttribute<T>();
            return !string.IsNullOrEmpty(table) ? table
                : throw new InvalidOperationException($"{typeof(T).Name} 未設定資料表");
        }

        private static string ForeignKey<TChild, TParent>()
        {
            string field = CommonUtility.GetForeignKeyField<TChild, TParent>();
            return !string.IsNullOrEmpty(field) ? field
                : throw new InvalidOperationException($"{typeof(TChild).Name} 未設定 {typeof(TParent).Name} 關聯");
        }

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

        /// <summary>本人有效列查詢；排序與鎖定由呼叫端指定，不建立隱性交易。</summary>
        protected async Task<List<T>> QuerySnapshotRowsAsync<T>(
            Expression<Func<T, object?>>? orderBy = null, bool lockRows = false,
            IEnumerable<Guid>? ids = null) where T : ModelBase
        {
            string filter = " AND Status != -1";
            if (ids != null) { filter += " AND Id IN @Ids"; }
            string order = orderBy == null ? "" : $" ORDER BY {Field(orderBy)}";
            string hint = lockRows ? " WITH (UPDLOCK, HOLDLOCK)" : "";
            return (await QueryAsync<T>($"SELECT * FROM {Table<T>()}{hint} WHERE CreateUserId = @CreateUserId{filter}{order}",
                new { CreateUserId = _playerId, Ids = ids })).ToList();
        }

        protected Task<T?> QuerySnapshotParentAsync<T>() where T : ModelBase =>
            QueryFirstAsync<T?>($"SELECT TOP 1 * FROM {Table<T>()} WHERE Status != -1 AND CreateUserId = @CreateUserId",
                new { CreateUserId = _playerId });

        /// <summary>批次查詢子列；父子雙重擁有權由 SQL 保證，各層狀態過濾由領域指定。</summary>
        protected async Task<List<TChild>> QuerySnapshotChildrenAsync<TChild, TParent>(
            IEnumerable<Guid>? parentIds = null, bool activeChildrenOnly = false, bool activeParentsOnly = true)
            where TChild : ModelBase where TParent : ModelBase
        {
            string foreignKey = ForeignKey<TChild, TParent>();
            string filter = activeChildrenOnly ? " AND c.Status != -1" : "";
            if (activeParentsOnly) { filter += " AND p.Status != -1"; }
            if (parentIds != null) { filter += $" AND c.{foreignKey} IN @ParentIds"; }
            return (await QueryAsync<TChild>($@"SELECT c.* FROM {Table<TChild>()} c
                INNER JOIN {Table<TParent>()} p ON p.Id = c.{foreignKey}
                WHERE p.CreateUserId = @CreateUserId AND c.CreateUserId = @CreateUserId{filter}
                ORDER BY c.Sequence", new { CreateUserId = _playerId, ParentIds = parentIds })).ToList();
        }

        protected Task<int> DeleteSnapshotRowsAsync<T>() where T : ModelBase =>
            DeleteOwnedRowsAsync(Table<T>());

        protected Task<int> DeleteSnapshotChildrenAsync<TChild, TParent>(Guid parentId) where TChild : ModelBase =>
            DeleteOwnedRowsAsync(Table<TChild>(), ForeignKey<TChild, TParent>(), parentId);

        /// <summary>呼叫端由最深層開始刪；內外層均限制本人，且不忽略已停用的舊明細。</summary>
        protected Task<int> DeleteSnapshotDescendantsAsync<TChild, TParent, TAncestor>(Guid ancestorId)
            where TChild : ModelBase where TParent : ModelBase =>
            DeleteOwnedRowsAsync(Table<TChild>(), ForeignKey<TChild, TParent>(), ancestorId,
                Table<TParent>(), ForeignKey<TParent, TAncestor>());

        /// <summary>每次集合序號從零開始；先插父列再回呼，讓明細可使用已產生的 Id。</summary>
        protected async Task InsertSnapshotRowsAsync<TInput, TRow>(IEnumerable<TInput> inputs,
            Func<TInput, int, TRow> map, Func<TInput, TRow, Task>? afterInsert = null) where TRow : ModelBase
        {
            int sequence = 0;
            foreach (TInput input in inputs)
            {
                TRow row = map(input, sequence);
                row.Sequence = sequence++;
                await InsertSnapshotRowAsync(row);
                if (afterInsert != null) { await afterInsert(input, row); }
            }
        }

        protected Task<int> InsertSnapshotRowAsync<TRow>(TRow row) where TRow : ModelBase
        {
            row.CreateUserId = _playerId;
            return InsertAsync(row);
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
