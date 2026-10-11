
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Utility;
using System.Threading.Tasks;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class MonsterSettingService : _ModelService<MonsterSetting, MonsterSettingModel>
    {
        public MonsterSettingService(BaseServiceArgument argument) : base(argument) { }

    }
}
