using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;
using System;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(Ability))]
    public class Ability : _AbilitySetting
    {
        [Relation(nameof(AbilitySetting))]
        public Guid? AbilitySettingId { get => Get<Guid?>(); set => Set(value); }
    }

}
