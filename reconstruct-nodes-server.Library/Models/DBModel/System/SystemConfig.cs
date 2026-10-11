using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(SystemConfig))]
    public class SystemConfig : ModelBase
    {
        public SystemConfig() 
        {
            Group = SystemConfigGroup.一般;
        }

        public SystemConfigGroup Group { get => Get<SystemConfigGroup>(); set => Set(value); }
        public string Key { get => Get<string>(); set => Set(value); }
        public string Value { get => Get<string>(); set => Set(value); }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<SystemConfig>()
                .HasIndex(b => b.Group);
        }
    }

    public enum SystemConfigGroup 
    { 
        一般 = 0,
        伺服器 = 1,
        伺服器連線數 = 2,
    }

}
