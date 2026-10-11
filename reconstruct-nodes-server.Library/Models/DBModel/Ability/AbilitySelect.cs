using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;
using System;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(AbilitySelect))]
    public class AbilitySelect : ModelBase
    {
        public string Name { get => Get<string>(); set => Set(value); }
        public string AbilityCategoryArray { get => Get<string>(); set => Set(value); }
    }

}
