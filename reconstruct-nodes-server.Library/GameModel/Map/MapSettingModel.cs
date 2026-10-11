using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using System.Collections.Generic;

namespace ReconstructNodesServer.Library.GameModel
{
    public class MapSettingModel : MapSetting 
    {
        public List<MonsterSettingModel> MonsterSettingList { get; set; } = new List<MonsterSettingModel>();
    }
}
