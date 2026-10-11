using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using System;
using System.Collections.Generic;

namespace ReconstructNodesServer.Library.GameModel
{
    public class PlayerModel : Player 
    {
        public List<SettingVersionModel> SettingVersionList { get; set; } = new List<SettingVersionModel>();
    }
}
