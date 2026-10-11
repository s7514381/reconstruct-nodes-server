using System;
using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(WeaponAffix))]
    public class WeaponAffix : ModelBase
    {
        [Relation(nameof(Weapon))]
        public Guid? WeaponId { get => Get<Guid?>(); set => Set(value); }

        public string AffixKey { get => Get<string>(); set => Set(value); }

        public bool Rolled { get => Get<bool>(); set => Set(value); }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<WeaponAffix>()
                .HasIndex(b => b.WeaponId);
        }
    }
}
