using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Library.Models;
using System;
using System.Collections.Generic;

namespace ReconstructNodesServer.Library.GameModel
{
    public class PlayerParamsModel : PlayerParams
    {
        public string Key { get { return $"{Group}_{Name}"; } }
    }
}
