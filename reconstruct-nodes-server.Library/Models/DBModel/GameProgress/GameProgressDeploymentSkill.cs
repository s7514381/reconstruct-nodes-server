using System;
using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(GameProgressDeploymentSkill))]
    public class GameProgressDeploymentSkill : ModelBase
    {
        [Relation(nameof(GameProgressDeployment))]
        public Guid? GameProgressDeploymentId { get => Get<Guid?>(); set => Set(value); }

        public int Kind { get => Get<int>(); set => Set(value); }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<GameProgressDeploymentSkill>()
                .HasIndex(b => b.GameProgressDeploymentId);
        }
    }
}
