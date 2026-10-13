using System;
using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(Deployment))]
    public class Deployment : ModelBase
    {
        public int SpotIndex { get => Get<int>(); set => Set(value); }

        public int? CellX { get => Get<int?>(); set => Set(value); }

        public int? CellY { get => Get<int?>(); set => Set(value); }

        [Relation(nameof(Character))]
        public Guid CharacterId { get => Get<Guid>(); set => Set(value); }

        public int Wave { get => Get<int>(); set => Set(value); }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Deployment>().HasIndex(row => row.CharacterId);
        }
    }
}
