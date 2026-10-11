using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using System.Collections.Generic;

namespace ReconstructNodesServer.Library.GameModel
{
    public class AbilityModel : Ability
    {
        public List<AbilityCardModel> AbilityCardList { get; set; } = new List<AbilityCardModel>();

    }
}
