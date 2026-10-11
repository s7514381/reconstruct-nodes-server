using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using System.Collections.Generic;

namespace ReconstructNodesServer.Library.GameModel
{
    public class AchievementModel : Achievement
    {
        public List<AchievementDetailModel> DetailList { get; set; } = new List<AchievementDetailModel>();
    }
}
