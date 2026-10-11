
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class AchievementDetailSettingService(BaseServiceArgument argument) : _SettingService<AchievementDetailSetting, AchievementDetailSettingModel>(argument)
    {
    }
}
