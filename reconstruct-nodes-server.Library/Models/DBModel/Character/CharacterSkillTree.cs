using System;
using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(CharacterSkillTree))]
    public class CharacterSkillTree : ModelBase
    {
        [Relation(nameof(Character))]
        public Guid CharacterId { get => Get<Guid>(); set => Set(value); }
        public int Kind { get => Get<int>(); set => Set(value); }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<CharacterSkillTree>().HasIndex(row => row.CharacterId);
        }
    }
}
