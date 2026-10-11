using System;
using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(WeaponUniqueValue))]
    public class WeaponUniqueValue : ModelBase
    {
        [Relation(nameof(Weapon))]
        public Guid? WeaponId { get => Get<Guid?>(); set => Set(value); }

        public string ValueKey { get => Get<string>(); set => Set(value); }

        public string Value { get => Get<string>(); set => Set(value); }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<WeaponUniqueValue>()
                .HasIndex(b => b.WeaponId);
        }
    }
}
