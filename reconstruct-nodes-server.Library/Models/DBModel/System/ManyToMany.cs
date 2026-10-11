#nullable enable
using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(ManyToMany))]
    public class ManyToMany : ModelBase
    {
        public string SourceTable { get => Get<string>(); set => Set(value); }

        public Guid? SourceId { get => Get<Guid?>(); set => Set(value); }

        [StringLength(100)]
        public string TargetTable { get => Get<string>(); set => Set(value); }

        public Guid? TargetId { get => Get<Guid?>(); set => Set(value); }

        public int? RelationType { get; set; }

        public string? Params { get; set; }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ManyToMany>()
                .HasIndex(b => b.SourceId);

            modelBuilder.Entity<ManyToMany>()
                .HasIndex(b => b.TargetTable);

            modelBuilder.Entity<ManyToMany>()
                .HasIndex(b => b.TargetId);
        }
    }

}
