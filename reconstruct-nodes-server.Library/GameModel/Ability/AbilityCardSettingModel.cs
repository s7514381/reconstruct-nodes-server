using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using System.Collections.Generic;

namespace ReconstructNodesServer.Library.GameModel
{
    public class AbilityCardSettingModel : AbilityCardSetting 
    {
        public List<AbilityProficiencySettingModel> ProficiencyList { get; set; } = new List<AbilityProficiencySettingModel>();
    }
}
