using System;
using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(CharacterSkillNode))]
    public class CharacterSkillNode : ModelBase
    {
        [Relation(nameof(CharacterSkillTree))]
        public Guid CharacterSkillTreeId { get => Get<Guid>(); set => Set(value); }
        public string NodeType { get => Get<string>(); set => Set(value); }
        public string LootId { get => Get<string>(); set => Set(value); }
        public int? Kind { get => Get<int?>(); set => Set(value); }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<CharacterSkillNode>().HasIndex(row => row.CharacterSkillTreeId);
        }
    }
}
