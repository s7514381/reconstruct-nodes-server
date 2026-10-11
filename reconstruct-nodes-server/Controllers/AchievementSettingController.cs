using Microsoft.AspNetCore.Mvc;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Argument;
using ReconstructNodesServer.Services;

namespace ReconstructNodesServer.Controllers
{
	public class AchievementSettingController(BaseApiArgument argument,
            AchievementSettingService service) : BaseSettingApiController<AchievementSetting, AchievementSettingModel>(argument, service)
    {
    }
}
