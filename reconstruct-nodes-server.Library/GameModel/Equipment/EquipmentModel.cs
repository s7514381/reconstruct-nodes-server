using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using System.Collections.Generic;

namespace ReconstructNodesServer.Library.GameModel
{
    public class EquipmentModel : Equipment
    {
        public List<ModifierModel> ModifierList { get; set; } = new List<ModifierModel>();
    }
}
