using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReconstructNodesServer.Library.GameModel
{
    public class AbilitySelectModel : AbilitySelect
    {
        public List<AbilityCategory> AbilityCategoryList { get; set; } = new List<AbilityCategory>();
    }
}
