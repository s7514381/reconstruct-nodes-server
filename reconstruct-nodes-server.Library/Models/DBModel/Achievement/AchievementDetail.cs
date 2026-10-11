using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;
using System;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(AchievementDetail))]
    public class AchievementDetail : _AchievementDetailSetting 
    {
        [Relation(nameof(Achievement))]
        public Guid? AchievementId { get => Get<Guid?>(); set => Set(value); }

        [Relation(nameof(AchievementDetailSetting))]
        public Guid? AchievementDetailSettingId { get => Get<Guid?>(); set => Set(value); }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<AchievementDetail>()
                .HasIndex(b => b.AchievementId);
        }
    }


}
