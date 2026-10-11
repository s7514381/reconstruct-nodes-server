using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Library.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(PlayerParams))]
    public class PlayerParams : ModelBase
    {
        public string Name { get => Get<string>(); set => Set(value); }
        public string Group { get => Get<string>(); set => Set(value); }
        public string Value { get => Get<string>(); set => Set(value); }
    }
}
