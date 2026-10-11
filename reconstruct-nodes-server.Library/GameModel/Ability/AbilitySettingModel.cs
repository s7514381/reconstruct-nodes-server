using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using System.Collections.Generic;

namespace ReconstructNodesServer.Library.GameModel
{
    public class AbilitySettingModel : AbilitySetting 
    {
        public List<AbilityCardSettingModel> AbilityCardList { get; set; } = new List<AbilityCardSettingModel>();
    }
}
