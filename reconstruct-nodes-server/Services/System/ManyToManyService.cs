
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Utility;
using System.Threading.Tasks;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class ManyToManyService : _ModelService<ManyToMany, ManyToManyModel>
    {
        public ManyToManyService(BaseServiceArgument argument) : base(argument) { }

        public async Task<IEnumerable<Guid>> GetIdsBySource(Guid id, string targetTable)
        {
            string sql = Sql(  select: $"SELECT TargetId ",
                             subWhere: $@"AND SourceId = @SourceId
                                          AND TargetTable = @TargetTable
                                          AND CreateUserId = @CreateUserId");
            return await QueryAsync<Guid>(sql, new { CreateUserId = _playerId, SourceId = id, TargetTable = targetTable });
        }

        public async Task SaveById<TParent, TModel>(TParent parent, List<TModel> models) 
            where TParent : ModelBase
            where TModel : ModelBase
        {
            await SaveById(parent, models.Select(x => x.Id).ToList(), CommonUtility.GetTableAttribute<TModel>());
        }

        public async Task SaveById<TParent>(TParent parent, List<Guid> ids, string targetTable) where TParent : ModelBase
        {
            List<Task> tasks = new();
            Guid parentId = parent.Id;
            string sourceTable = CommonUtility.GetTableAttribute<TParent>();
            HashSet<Guid> hashIds = [.. ids];

            IEnumerable<Guid>? existIds = await GetIdsBySource(parentId, targetTable);
            HashSet<Guid> hashExistIds = [.. existIds];

            //檢查是否有被刪除的
            foreach (var item in hashExistIds) 
            {
                tasks.AddTask(async () =>
                {
                    if (!hashIds.Contains(item)) 
                    { 
                        string sql = $@"DELETE FROM {_tableName}
                                         WHERE CreateUserId = @CreateUserId
                                           AND SourceTable = @SourceTable
                                           AND SourceId = @SourceId
                                           AND TargetTable = @TargetTable
                                           AND TargetId = @TargetId";
                        await ExecuteAsync(sql, new { CreateUserId = _playerId, SourceTable = sourceTable, SourceId = parentId, TargetTable = targetTable, TargetId = item });
                    }
                });
            }
            //檢查是否有新增的
            foreach (var item in hashIds)
            {
                tasks.AddTask(async () =>
                {
                    if (!hashExistIds.Contains(item))
                    {
                        ManyToMany many = new() {
                            SourceTable = sourceTable,
                            SourceId = parentId,
                            TargetTable = targetTable,
                            TargetId = item,
                            CreateUserId = _playerId,
                        };
                        await InsertAsync(many);
                    }
                });
            }
            await Task.WhenAll(tasks);
        }

    }
}
