using System;
using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(DeploymentSkill))]
    public class DeploymentSkill : ModelBase
    {
        [Relation(nameof(Deployment))]
        public Guid? DeploymentId { get => Get<Guid?>(); set => Set(value); }

        public int Kind { get => Get<int>(); set => Set(value); }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DeploymentSkill>()
                .HasIndex(b => b.DeploymentId);
        }
    }
}
