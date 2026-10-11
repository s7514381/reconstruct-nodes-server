
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class SettingVersionService : _SettingService<SettingVersion, SettingVersionModel>
    {

        public SettingVersionService(BaseServiceArgument argument) : base(argument) { }

    }
}
