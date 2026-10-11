
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using System.Threading.Tasks;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class SystemConfigService : _SettingService<SystemConfig, SystemConfigModel>
    {
        public SystemConfigService(BaseServiceArgument argument) : base(argument) { }

        public virtual async Task<SystemConfigModel> GetModelByKey(SystemConfigGroup group, string key)
        {
            string sql = Sql(subWhere: $@"AND Group = @Group
                                          AND Key = @Key");
            SystemConfigModel result = await QueryFirstAsync<SystemConfigModel>(sql, new { Group = group, Key = key });
            await SetModel(result);

            return result;
        }

        public virtual async Task<IEnumerable<SystemConfigModel>> GetModelListByGroup(SystemConfigGroup group)
        {
            string sql = Sql(subWhere: $"AND Group = @Group");

            IEnumerable<SystemConfigModel> list = await QueryAsync<SystemConfigModel>(sql, new { Group = group });
            return await SetModelList(list);
        }

        public override async Task Seed()
        {
            SystemConfigModel config = new()
            {
                Group = SystemConfigGroup.伺服器,
                Key = "1",
                Value = "https://timwu19950429-cjfggjb9eactfvf9.japaneast-01.azurewebsites.net/",
            };
            await InsertModel(config);

            config = new()
            {
                Group = SystemConfigGroup.伺服器連線數,
                Key = "1",
                Value = "0",
            };
            await InsertModel(config);
        }

    }
}
