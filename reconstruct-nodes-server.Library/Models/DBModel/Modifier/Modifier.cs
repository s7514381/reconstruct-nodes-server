using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Models.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(Modifier))]
    public class Modifier : ModelBase
    {
        public ModifierType Type { get => Get<ModifierType>(); set => Set(value); }
        public float Value { get => Get<float>(); set => Set(value); }
        public int Tier { get => Get<int>(); set => Set(value); }
    }

}