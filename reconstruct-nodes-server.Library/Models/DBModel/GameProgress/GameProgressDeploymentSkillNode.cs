using System;
using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(GameProgressDeploymentSkillNode))]
    public class GameProgressDeploymentSkillNode : ModelBase
    {
        [Relation(nameof(GameProgressDeploymentSkill))]
        public Guid? GameProgressDeploymentSkillId { get => Get<Guid?>(); set => Set(value); }

        /// <summary>auxiliary 或 active，決定 LootId 與 Kind 的語意。</summary>
        public string NodeType { get => Get<string>(); set => Set(value); }

        public string LootId { get => Get<string>(); set => Set(value); }

        public int? Kind { get => Get<int?>(); set => Set(value); }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<GameProgressDeploymentSkillNode>()
                .HasIndex(b => b.GameProgressDeploymentSkillId);
        }
    }
}
