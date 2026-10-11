using System;
using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(AchievementCollected))]
    public class AchievementCollected : ModelBase
    {
        [Relation(nameof(Achievement))]
        public Guid? AchievementId { get => Get<Guid?>(); set => Set(value); }

        public string Name { get => Get<string>(); set => Set(value); }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<AchievementCollected>()
                .HasIndex(b => b.AchievementId);
        }
    }
}
