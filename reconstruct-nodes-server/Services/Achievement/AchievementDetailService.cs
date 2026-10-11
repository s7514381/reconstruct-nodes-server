
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class AchievementDetailService(BaseServiceArgument argument) : _ModelService<AchievementDetail, AchievementDetailModel>(argument)
    {
        public async Task<List<AchievementDetail>> Reward(List<Guid> ids)
        {
            string sql = $@"UPDATE {_tableName} 
                               SET IsReward = 1 
                            OUTPUT Inserted.* 
                             WHERE CreateUserId = @CreateUserId
                               AND {_keyField} in @Ids";
            var list = await QueryAsync<AchievementDetail>(sql, new { CreateUserId = _playerId, Ids = ids });
            return list.ToList();
        }
    }
}
