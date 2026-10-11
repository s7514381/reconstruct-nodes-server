using System;
using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(TalentLegacyCategory))]
    public class TalentLegacyCategory : ModelBase
    {
        [Relation(nameof(Talent))]
        public Guid? TalentId { get => Get<Guid?>(); set => Set(value); }

        public string TreeId { get => Get<string>(); set => Set(value); }

        public string NodeId { get => Get<string>(); set => Set(value); }

        public string Value { get => Get<string>(); set => Set(value); }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<TalentLegacyCategory>()
                .HasIndex(b => b.TalentId);
        }
    }
}
