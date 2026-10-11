using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Models.Enum;
using System;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(AbilityProficiencySetting))]
    public class AbilityProficiencySetting : _AbilityProficiencySetting { }

    public class _AbilityProficiencySetting : ModelBase
    {
        public string Name { get => Get<string>(); set => Set(value); }
        public ProficiencyCategory Category { get => Get<ProficiencyCategory>(); set => Set(value); }
        public string SpritePath { get => Get<string>(); set => Set(value); }
    }
}
