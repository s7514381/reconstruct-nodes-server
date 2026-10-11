using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;
using System;


namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(AbilityCard))]
    public class AbilityCard : _AbilityCardSetting
    {
        [Relation(nameof(Ability))]
        public Guid? AbilityId { get => Get<Guid?>(); set => Set(value); }

        [Relation(nameof(AbilityCardSetting))]
        public Guid? AbilityCardSettingId { get => Get<Guid?>(); set => Set(value); }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<AbilityCard>()
                .HasIndex(b => b.AbilityId);
        }
    }

}
