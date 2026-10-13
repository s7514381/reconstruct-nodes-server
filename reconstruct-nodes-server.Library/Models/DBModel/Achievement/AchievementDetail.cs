using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;
using ReconstructNodesServer.Library.Models.Enum;
using System;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(AchievementDetail))]
    public class AchievementDetail : _AchievementDetailSetting 
    {
        [Relation(nameof(Achievement))]
        public Guid? AchievementId { get => Get<Guid?>(); set => Set(value); }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<AchievementDetail>()
                .HasIndex(b => b.AchievementId);
        }
    }

    /// <summary>成就明細共用基底欄位，由 AchievementDetail 繼承；舊設定表 AchievementDetailSetting 已移除</summary>
    public class _AchievementDetailSetting : ModelBase
    {
        public string Name { get => Get<string>(); set => Set(value); }
        public int Target { get => Get<int>(); set => Set(value); }
        public bool IsReward { get => Get<bool>(); set => Set(value); }
        public DateTime? RewardDate { get => Get<DateTime?>(); set => Set(value); }
        public float? GainCoins { get => Get<float?>(); set => Set(value); }
        public AchievementType? Type { get => Get<AchievementType?>(); set => Set(value); }
        public string Version { get => Get<string>(); set => Set(value); } 
    }
}
