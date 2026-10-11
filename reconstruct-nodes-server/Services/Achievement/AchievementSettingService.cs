
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;

namespace ReconstructNodesServer.Services
{
    public class AchievementSettingService(BaseServiceArgument argument) : _SettingService<AchievementSetting, AchievementSettingModel>(argument)
    {
        private AchievementDetailSettingService _settingService => _lazy.AchievementDetailSettingService.Value;

        protected override async Task SetModel(AchievementSettingModel viewModel)
        {
            viewModel.DetailList = await _settingService.GetModelListByParent<AchievementSetting>(viewModel.Id);
        }

    }
}
