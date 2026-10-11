using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using System.Collections.Generic;

namespace ReconstructNodesServer.Library.GameModel
{
    public class AchievementSettingModel : AchievementSetting 
    {
        public List<AchievementDetailSettingModel> DetailList { get; set; } = new List<AchievementDetailSettingModel>();
    }
}
