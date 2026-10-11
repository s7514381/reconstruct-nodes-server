using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Models.Enum;
using System;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(AbilityCardSetting))]
    public class AbilityCardSetting : _AbilityCardSetting { }

    public class _AbilityCardSetting : ModelBase
    {
        [Relation(nameof(AbilitySetting))]
        public Guid? AbilitySettingId { get => Get<Guid?>(); set => Set(value); }
        public AbilityCardUsageType? UsageType { get => Get<AbilityCardUsageType?>(); set => Set(value); }
        public int Category { get => Get<int>(); set => Set(value); }
        public float? Weights { get => Get<float?>(); set => Set(value); }
        public string Name { get => Get<string>(); set => Set(value); }
        public float? Level { get => Get<float?>(); set => Set(value); }
        public float? MaxLevel { get => Get<float?>(); set => Set(value); }
        public float? CurrentExp { get => Get<float?>(); set => Set(value); }
        public bool? IsOwned { get => Get<bool?>(); set => Set(value); }
        public float? Grade { get => Get<float?>(); set => Set(value); }
        public float? Attack { get => Get<float?>(); set => Set(value); }
    }
}
