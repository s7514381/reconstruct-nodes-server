using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using System;
using System.Collections.Generic;

namespace ReconstructNodesServer.Library.GameModel
{
    public class PlayerValidateModel : Player 
    {
        public DateTime? ExpireTime { get; set; }
    }
}
