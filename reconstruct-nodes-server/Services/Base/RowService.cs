using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Utility;
using ReconstructNodesServer.Repositories;
using System.Linq.Expressions;
using System.Reflection;

namespace ReconstructNodesServer.Services
{
    /// <summary>
    /// 瘦基底：單一 DBModel 的 CRUD、軟刪除、Seed 與交易輔助。純子表直接繼承此類別。
    /// </summary>
    public abstract class _RowService<DBModel> : BaseService
        where DBModel : ModelBase, new()
    {
        public string? _tableName;
        public string? _keyField;
        protected bool softDelete { get; set; } = true;

        public _RowService(BaseServiceArgument argument) : base(argument)
        {
            _tableName = CommonUtility.GetTableAttribute<DBModel>();
            _keyField = CommonUtility.GetKeyAttribute<DBModel>();
        }

        public virtual string SqlData()
        {
            return $"SELECT * FROM {_tableName} WHERE {_keyField} = @Id";
        }

        public virtual async Task<DBModel> GetData(Guid id)
        {
            DBModel result = await QueryFirstAsync<DBModel>(SqlData(), new { Id = id });
            return result;
        }

        public virtual string Sql(
            string select = "", string from = "", string join = "", 
            string where = "", string subWhere = "", string order = "")
        {
            if (string.IsNullOrEmpty(select)) { select = $"SELECT main.* ";  }
            if (string.IsNullOrEmpty(from)) { from = $"FROM {_tableName} AS main "; }
            if (string.IsNullOrEmpty(where)) { where = $"WHERE main.[Status] != -1"; }

            return $@"{select} {from} {join} {where} {subWhere} {order}"; 
        }

        /// <summary>
        /// 本服務 DBModel 的「本人有效列」查詢，與 <see cref="OwnedSql"/> 同一份規則（無別名、不 JOIN 的簡單情境）；
        /// 需要別名 / JOIN 的情境仍用 <see cref="Sql"/>。
        /// </summary>
        public string SelectOwned(string? condition = null, string columns = "*", string? orderBy = null, int? top = null) =>
            OwnedSql.Select<DBModel>(condition, columns, orderBy, top);

        public virtual string SqlDataList(string subWhere = "")
        {
            return Sql(subWhere: $"AND CreateUserId = @CreateUserId {subWhere}");
        }

        public virtual async Task<IEnumerable<DBModel>> GetDataList()
        {
            return await QueryAsync<DBModel>(SqlDataList(), new { CreateUserId = _playerId });
        }

        /// <summary>
        /// 資料是否存在
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public virtual async Task<bool> IsExist(Guid id)
        {
            return await QueryFirstAsync<int>($@"
                SELECT COUNT(1) 
                  FROM {_tableName} WITH (NOLOCK)
                 WHERE {_keyField} = @KeyValue", new { KeyValue = id }) > 0;
        }

        public virtual async Task SaveData(DBModel model)
        {
            List<DBModel> list = new() { model };
            await SaveData(list);
        }

        public virtual async Task SaveData(IEnumerable<DBModel> list)
        {
            if (list?.Any() != true) return;

            List<Task> tasks = new();
            HashSet<Guid> hashExistIds = await GetExistIdSet(list.Select(x => x.Id));

            foreach (var item in list)
            {
                tasks.AddTask(async () => {
                    if (hashExistIds.Contains(item.Id)) { await UpdateAsync(item); }
                    else { await InsertAsync(item); }
                });
            }
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 查詢指定 Id 中已存在於資料庫的集合，供 Save / SaveData 判斷新增或更新；僅計入本人資料，他人 Id 不會被當成既有資料而覆寫
        /// </summary>
        protected async Task<HashSet<Guid>> GetExistIdSet(IEnumerable<Guid> ids)
        {
            string sql = Sql(subWhere: $" AND CreateUserId = @CreateUserId AND {_keyField} in @IdList");
            IEnumerable<Guid> existIds = await QueryAsync<Guid>(sql, new { CreateUserId = _playerId, IdList = ids }, "IsExist");
            return [.. existIds];
        }

        public virtual string SqlDelete(string subWhere) 
        {
            if (string.IsNullOrEmpty(subWhere)) { subWhere = " and 1 = 0 ";  }
            string sql;

            if (softDelete) { sql = $@"update {_tableName} set Status = -1 where Status != -1 "; }
            else { sql = $@"delete {_tableName} where Status != -1 "; }
            sql += subWhere;

            return sql;
        }

        /// <summary>
        /// 自然鍵欄位名稱（例如 EntryKey、ItemKey），使用 DeleteByNaturalKey 前需由子類別指定
        /// </summary>
        protected virtual string? NaturalKeyField => null;

        /// <summary>
        /// 依自然鍵軟刪除本人資料；需要額外檢查時由子類別 override 並呼叫 base
        /// </summary>
        /// <param name="naturalKey">自然鍵值</param>
        /// <returns>受影響筆數</returns>
        public virtual async Task<int> DeleteByNaturalKey(string naturalKey)
        {
            if (string.IsNullOrEmpty(NaturalKeyField))
            {
                throw new InvalidOperationException($"{GetType().Name} 未設定 NaturalKeyField");
            }

            string sql = SqlDelete($"and CreateUserId = @CreateUserId and {NaturalKeyField} = @NaturalKey");
            return await ExecuteAsync(sql, new { CreateUserId = _playerId, NaturalKey = naturalKey });
        }

        /// <summary>
        /// 硬刪除本人擁有的列，可選擇再以父鍵欄位限定範圍
        /// </summary>
        protected async Task<int> DeleteOwnedRowsAsync(string tableName, string? parentColumn = null, Guid? parentId = null,
            string? ownedParentTable = null, string? ancestorColumn = null)
        {
            string sql = $"DELETE FROM {tableName} WHERE CreateUserId = @CreateUserId";
            if (ownedParentTable != null)
            {
                if (string.IsNullOrEmpty(parentColumn) || string.IsNullOrEmpty(ancestorColumn))
                {
                    throw new ArgumentException("關聯刪除必須指定父鍵與祖先鍵");
                }
                sql += $" AND {parentColumn} IN (SELECT Id FROM {ownedParentTable} WHERE {ancestorColumn} = @ParentId AND CreateUserId = @CreateUserId)";
            }
            else if (!string.IsNullOrEmpty(parentColumn)) { sql += $" AND {parentColumn} = @ParentId"; }

            return await ExecuteAsync(sql, new { CreateUserId = _playerId, ParentId = parentId });
        }

        /// <summary>
        /// 篩出指定 Id 中屬於目前玩家的集合
        /// </summary>
        protected async Task<HashSet<Guid>> GetOwnedIdSet(IEnumerable<Guid> ids)
        {
            string sql = $"SELECT {_keyField} FROM {_tableName} WHERE CreateUserId = @CreateUserId AND {_keyField} IN @IdList";
            IEnumerable<Guid> ownedIds = await QueryAsync<Guid>(sql, new { CreateUserId = _playerId, IdList = ids });
            return [.. ownedIds];
        }

        // 僅接受直接屬性，識別字一律來自模型，不接受外部 SQL。
        protected static string Field<T, TValue>(Expression<Func<T, TValue>> selector)
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

        private static string TableOf<T>() => OwnedSql.Table<T>();

        private static string ForeignKeyTo<TParent>()
        {
            string field = CommonUtility.GetForeignKeyField<DBModel, TParent>();
            return !string.IsNullOrEmpty(field) ? field
                : throw new InvalidOperationException($"{typeof(DBModel).Name} 未設定 {typeof(TParent).Name} 關聯");
        }

        /// <summary>本人有效列查詢；排序與鎖定由呼叫端指定，不建立隱性交易。</summary>
        public async Task<List<DBModel>> QueryOwnedRowsAsync(
            Expression<Func<DBModel, object?>>? orderBy = null, bool lockRows = false,
            IEnumerable<Guid>? ids = null)
        {
            string sql = OwnedSql.Select<DBModel>(ids != null ? "Id IN @Ids" : null,
                orderBy: orderBy == null ? null : Field(orderBy), tableHint: lockRows ? "UPDLOCK, HOLDLOCK" : null);
            return (await QueryAsync<DBModel>(sql, new { CreateUserId = _playerId, Ids = ids })).ToList();
        }

        public Task<DBModel?> QueryOwnedFirstAsync() =>
            QueryFirstAsync<DBModel?>(OwnedSql.Select<DBModel>(top: 1), new { CreateUserId = _playerId });

        /// <summary>批次查詢子列；父子雙重擁有權由 SQL 保證，各層狀態過濾由領域指定。</summary>
        public async Task<List<DBModel>> QueryChildrenAsync<TParent>(
            IEnumerable<Guid>? parentIds = null, bool activeChildrenOnly = false, bool activeParentsOnly = true)
            where TParent : ModelBase
        {
            string? condition = parentIds != null ? $"c.{ForeignKeyTo<TParent>()} IN @ParentIds" : null;
            return (await QueryAsync<DBModel>(
                OwnedSql.Children<DBModel, TParent>(activeChildrenOnly, activeParentsOnly, condition),
                new { CreateUserId = _playerId, ParentIds = parentIds })).ToList();
        }

        public Task<int> DeleteAllOwnedAsync() =>
            DeleteOwnedRowsAsync(TableOf<DBModel>());

        public Task<int> DeleteChildrenAsync<TParent>(Guid parentId) =>
            DeleteOwnedRowsAsync(TableOf<DBModel>(), ForeignKeyTo<TParent>(), parentId);

        /// <summary>呼叫端由最深層開始刪；內外層均限制本人，且不忽略已停用的舊明細。</summary>
        public Task<int> DeleteDescendantsAsync<TParent, TAncestor>(Guid ancestorId) where TParent : ModelBase =>
            DeleteOwnedRowsAsync(TableOf<DBModel>(), ForeignKeyTo<TParent>(), ancestorId,
                TableOf<TParent>(), CommonUtility.GetForeignKeyField<TParent, TAncestor>());

        /// <summary>每次集合序號從零開始；先插父列再回呼，讓明細可使用已產生的 Id。</summary>
        public async Task InsertOwnedRowsAsync<TInput>(IEnumerable<TInput> inputs,
            Func<TInput, int, DBModel> map, Func<TInput, DBModel, Task>? afterInsert = null)
        {
            int sequence = 0;
            foreach (TInput input in inputs)
            {
                DBModel row = map(input, sequence);
                row.Sequence = sequence++;
                await InsertOwnedRowAsync(row);
                if (afterInsert != null) { await afterInsert(input, row); }
            }
        }

        public Task<int> InsertOwnedRowAsync(DBModel row)
        {
            row.CreateUserId = _playerId;
            return InsertAsync(row);
        }

        public virtual async Task Seed() { }
    }
}
