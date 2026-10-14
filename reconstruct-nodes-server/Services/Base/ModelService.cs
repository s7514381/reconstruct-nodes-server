using Azure;
using Azure.Data.Tables;
using Newtonsoft.Json;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Utility;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public abstract class _ModelService<DBModel, ViewModel> : BaseService
        where DBModel : ModelBase, new()
        where ViewModel : DBModel, new()
    {
        public string? _tableName;
        public string? _keyField;
        public Guid? _playerId => _player?.Id;
        public Player? _player => _lazy.PlayerService.Value.Player;
        protected bool softDelete { get; set; } = true;

        public _ModelService(BaseServiceArgument argument) : base(argument)
        {
            _tableName = CommonUtility.GetTableAttribute<DBModel>();
            _keyField = CommonUtility.GetKeyAttribute<DBModel>();
        }

        /// <summary>
        /// 鎖定目前玩家的 Player 列，序列化同一玩家的進度寫入與武器刪除，需在交易內呼叫
        /// </summary>
        protected async Task LockPlayerAsync()
        {
            await QueryAsync<Guid>("SELECT Id FROM Player WITH (UPDLOCK, HOLDLOCK) WHERE Id = @PlayerId", new { PlayerId = _playerId });
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

        public virtual async Task<ViewModel?> GetModel(Guid id)
        {
            ViewModel viewModel = await QueryFirstAsync<ViewModel>(SqlData(), new { Id = id });
            if (viewModel != null) { await SetModel(viewModel); }

            return viewModel;
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

        public virtual string SqlDataList(string subWhere = "")
        {
            return Sql(subWhere: $"AND CreateUserId = @CreateUserId {subWhere}");
        }

        public virtual async Task<IEnumerable<DBModel>> GetDataList()
        {
            return await QueryAsync<DBModel>(SqlDataList(), new { CreateUserId = _playerId });
        }

        public virtual async Task<IEnumerable<ViewModel>> GetModelList()
        {
            IEnumerable<ViewModel> list = await QueryAsync<ViewModel>(SqlDataList(), new { CreateUserId = _playerId });
            return await SetModelList(list);
        }

        public virtual async Task<IEnumerable<ViewModel>> GetModelList(IEnumerable<Guid> ids)
        {
            string sql = SqlDataList(subWhere: $"AND Id in @Ids");
            IEnumerable<ViewModel> list = await QueryAsync<ViewModel>(sql, new { Ids = ids, CreateUserId = _playerId });
            return await SetModelList(list);
        }

        public virtual string SqlMTMDataList(string subWhere = "")
        {
            return Sql(join: $@"inner join ManyToMany on main.Id = ManyToMany.TargetId",
                   subWhere: $@"AND ManyToMany.SourceId = @SourceId {subWhere}");
        }

        public virtual async Task<IEnumerable<DBModel>> GetMTMDataList(Guid sourceId)
        {
            return await QueryAsync<DBModel>(SqlMTMDataList(), new { SourceId = sourceId, CreateUserId = _playerId });
        }

        /// <summary>
        /// 多對多專用，傳入SourceId(父Id)
        /// </summary>
        /// <param name="subWhere"></param>
        /// <returns></returns>
        public virtual async Task<IEnumerable<ViewModel>> GetMTMModelList(Guid sourceId)
        {
            IEnumerable<ViewModel> list = await QueryAsync<ViewModel>(SqlMTMDataList(), new { SourceId = sourceId, CreateUserId = _playerId });
            return await SetModelList(list);
        }

        public virtual async Task<IEnumerable<ViewModel>> SetModelList(IEnumerable<ViewModel> list)
        {
            if (list == null || list.Count() == 0) { return list ?? []; }
            List<Task> tasks = new();

            foreach (ViewModel model in list)
            {
                tasks.AddTask(async () => { await SetModel(model); });
            }
            if (tasks.Count > 0) { await Task.WhenAll(tasks); }
            return list;
        }

        public virtual async Task<List<ViewModel>> GetModelListByParent<TParent>(Guid? parentId)
        {
            string foreignKeyField = CommonUtility.GetForeignKeyField<ViewModel, TParent>();
            if (string.IsNullOrEmpty(foreignKeyField)) { return new(); }

            string sql = Sql(subWhere: $"AND {foreignKeyField} = @ParentId ", 
                                order: $"ORDER BY Sequence");
            IEnumerable<ViewModel> list = await QueryAsync<ViewModel>(sql, new { ParentId = parentId });

            List<Task> tasks = new();
            int i = 0;
            foreach (ViewModel model in list)
            {
                //如果是子項目，則套用排序
                if (!model.Sequence.HasValue) { model.Sequence = i++; }
            }
            await SetModelList(list);

            return list.ToList();
        }

        /// <summary>
        /// 設定完整Model
        /// </summary>
        /// <param name="viewModel"></param>
        /// <returns></returns>
        protected virtual async Task SetModel(ViewModel viewModel) { }

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

        /// <summary>
        /// 完整儲存，沒有特殊需求一律呼叫這支
        /// </summary>
        /// <param name="viewModel"></param>
        /// <returns></returns>
        public virtual async Task Save(ViewModel viewModel)
        {
            List<ViewModel> list = new() { viewModel };
            await Save(list);
        }

        public virtual async Task Save(IEnumerable<ViewModel> list)
        {
            if (list?.Any() != true) return;

            List<Task> tasks = new();
            HashSet<Guid> hashExistIds = await GetExistIdSet(list.Select(x => x.Id));

            foreach (var item in list)
            {
                tasks.AddTask(async () => {
                    if (hashExistIds.Contains(item.Id)) { await UpdateModel(item); }
                    else { await InsertModel(item); }
                });
            }
            await Task.WhenAll(tasks);
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
        private async Task<HashSet<Guid>> GetExistIdSet(IEnumerable<Guid> ids)
        {
            string sql = Sql(subWhere: $" AND CreateUserId = @CreateUserId AND {_keyField} in @IdList");
            IEnumerable<Guid> existIds = await QueryAsync<Guid>(sql, new { CreateUserId = _playerId, IdList = ids }, "IsExist");
            return [.. existIds];
        }

        protected virtual async Task UpdateModel(ViewModel viewModel)
        {
            viewModel.CreateUserId = _playerId;
            await ModelSave(viewModel);

            DBModel DBModel = viewModel;
            await UpdateAsync(DBModel);
        }

        protected virtual async Task InsertModel(ViewModel viewModel)
        {
            viewModel.CreateUserId = _playerId;
            await ModelSave(viewModel);

            DBModel DBModel = viewModel;
            await InsertAsync(DBModel);
        }

        /// <summary>
        /// 儲存時更新物件資訊
        /// </summary>
        /// <param name="viewModel"></param>
        /// <returns></returns>
        protected virtual async Task ModelSave(ViewModel viewModel) { }

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

        protected virtual async Task ModelDelete(ViewModel model) { }

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
        /// 刪除
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public virtual async Task DeleteModel(ViewModel viewModel)
        {
            List<ViewModel> list = new() { viewModel };
            await DeleteModel(list);
        }

        public virtual async Task DeleteModel(IEnumerable<ViewModel> list)
        {
            if (list == null || list.Count() == 0) { return; }

            // 只刪除本人擁有的資料，非本人 Id 靜默略過（與 GetModelList 行為一致）
            HashSet<Guid> ownedIds = await GetOwnedIdSet(list.Select(x => x.Id));
            List<ViewModel> ownedList = list.Where(x => ownedIds.Contains(x.Id)).ToList();
            if (ownedList.Count == 0) { return; }

            List<Task> tasks = new();

            foreach (var item in ownedList)
            {
                tasks.AddTask(async () => { await ModelDelete(item); });
            }

            tasks.AddTask(async () =>
            {
                string sql = SqlDelete($"and CreateUserId = @CreateUserId and {_keyField} in @IdList");
                await ExecuteAsync(sql, new { CreateUserId = _playerId, IdList = ownedList.Select(x => x.Id) });
            });

            List<TableEntity> entities = new();
            foreach (var item in ownedList)
            {
                DBModel model = item.CastBy<DBModel>();
                var entity = new TableEntity(_tableName, item.Id.ToString())
                {
                    { "JsonData", JsonConvert.SerializeObject(model) },
                    { "CreateUserId", _playerId }
                };
                entities.Add(entity);
            }

            _ = Task.Run(async () => {
                await AzureTableTransfer(TableClientType.Delete, entities, async (service, entity) =>
                {
                    string sql = $"delete {entity.PartitionKey} where Id = @Id";
                    await service.ExecuteAsync(sql, new { Id = entity.RowKey });
                });
            });

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 篩出指定 Id 中屬於目前玩家的集合
        /// </summary>
        private async Task<HashSet<Guid>> GetOwnedIdSet(IEnumerable<Guid> ids)
        {
            string sql = $"SELECT {_keyField} FROM {_tableName} WHERE CreateUserId = @CreateUserId AND {_keyField} IN @IdList";
            IEnumerable<Guid> ownedIds = await QueryAsync<Guid>(sql, new { CreateUserId = _playerId, IdList = ids });
            return [.. ownedIds];
        }

        public virtual async Task TestSave()
        {
            IEnumerable<ViewModel> list = await GetModelList();
            if (list.Count() > 0) { await Save(list.First()); }
        }

        public virtual async Task TestSaveList()
        {
            IEnumerable<ViewModel> list = await GetModelList();
            foreach (var item in list) {
                item.CreateDate = DateTime.Now;
            }
            await Save(list);
        }

        public virtual async Task TestDelete()
        {
            IEnumerable<ViewModel> list = await GetModelList();
            if (list.Count() > 2) {
                await DeleteModel(list.Take(2));
            }
        }

        /// <summary>
        /// 由父項呼叫，更新明細
        /// </summary>
        /// <typeparam name="TParent"></typeparam>
        /// <param name="parent">父model</param>
        /// <param name="list"></param>
        /// <returns></returns>
        public virtual async Task UpdateDetail<TParent>(TParent parent, List<ViewModel> list)
        {
            //取得外來鍵
            string foreignKeyField = CommonUtility.GetForeignKeyField<ViewModel, TParent>();
            if (string.IsNullOrEmpty(foreignKeyField)) { throw new Exception(); }

            string parentKeyField = CommonUtility.GetKeyAttribute<TParent>();
            if (string.IsNullOrEmpty(parentKeyField)) { throw new Exception(); }

            object? foreignKeyValue = parent?.GetFieldValue(parentKeyField);
            if (foreignKeyValue == null) { throw new Exception(); }

            await UpdateDetail(list, foreignKeyField, foreignKeyValue);
        }

        /// <summary>
        /// 由父項呼叫，更新明細
        /// </summary>
        /// <typeparam name="TParent"></typeparam>
        /// <param name="parent">父model</param>
        /// <param name="list"></param>
        /// <returns></returns>
        protected virtual async Task UpdateDetail(List<ViewModel> list, string foreignKeyField, object foreignKeyValue)
        {
            if (list == null
                || string.IsNullOrEmpty(foreignKeyField)
                || foreignKeyValue == null) { return; }

            List<Guid> idList = new();
            List<Task> tasks = new();

            foreach (ViewModel item in list)
            {
                if (item.GetFieldValue(foreignKeyField) != foreignKeyValue)
                    item.SetFieldValue(foreignKeyField, foreignKeyValue);

                //還有在使用的明細不刪除
                idList.Add(item.Id);
            }
            tasks.AddTask(async () => { await Save(list); });

            //刪除沒用到的細項
            string sql = SqlDelete($@"and {foreignKeyField} = @ForeignKeyValue
                                      and {_keyField} not in @IdList
                                      and CreateUserId = @CreateUserId");
            tasks.AddTask(async () =>
            {
                await ExecuteAsync(sql, new
                {
                    ForeignKeyValue = foreignKeyValue,
                    IdList = idList,
                    CreateUserId = _playerId,
                });
            });
            await Task.WhenAll(tasks);
        }

        public virtual ViewModel MapSetting<TSetting>(TSetting setting) where TSetting : ModelBase, new()
        {
            return setting.MapSetting<ViewModel, TSetting>();
        }

        public async Task AzureTableTransfer(TableClientType tableClientType, List<TableEntity> entities, Func<_ModelService<DBModel, ViewModel>, TableEntity, Task> func) 
        {
            await _lazy.AzureStoreService.Value
                .Transfer<_ModelService<DBModel, ViewModel>>(tableClientType, GetType(), entities, async (service, entity) =>
            {
                await func(service, entity);
            });
        }

        public virtual async Task Seed() { }

    }

}