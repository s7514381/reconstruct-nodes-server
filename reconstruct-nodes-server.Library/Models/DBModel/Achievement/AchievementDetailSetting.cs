using Dapper.Contrib.Extensions;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Models.Enum;
using System;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(AchievementDetailSetting))]
    public class AchievementDetailSetting : _AchievementDetailSetting { }

    public class _AchievementDetailSetting : ModelBase
    {
        [Relation(nameof(AchievementSetting))]
        public Guid? AchievementSettingId { get => Get<Guid?>(); set => Set(value); }
        public string Name { get => Get<string>(); set => Set(value); }
        public int Target { get => Get<int>(); set => Set(value); }
        public bool IsReward { get => Get<bool>(); set => Set(value); }
        public DateTime? RewardDate { get => Get<DateTime?>(); set => Set(value); }
        public float? GainCoins { get => Get<float?>(); set => Set(value); }
        public AchievementType? Type { get => Get<AchievementType?>(); set => Set(value); }
        public string Version { get => Get<string>(); set => Set(value); } 
    }
}
