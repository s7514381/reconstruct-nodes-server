using Dapper.Contrib.Extensions;
using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.GameModel;
using ReconstructNodesServer.Library.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace ReconstructNodesServer.Library.DBModel
{
    [Table(nameof(Player))]
    public class Player : ModelBase
    {
        public string Name { get => Get<string>(); set => Set(value); }
        public string Account { get => Get<string>(); set => Set(value); }
        public byte[] Password { get => Get<byte[]>(); set => Set(value); }

        public Guid? Token { get => Get<Guid?>(); set => Set(value); }

        public string GoogleId { get => Get<string>(); set => Set(value); }
        public string Email { get => Get<string>(); set => Set(value); }
        public Guid? EmailVerifyId { get => Get<Guid?>(); set => Set(value); }
        public string NationalId { get => Get<string>(); set => Set(value); }
        public string ClientId { get => Get<string>(); set => Set(value); }
        public int? InventorySlotCount { get => Get<int?>(); set => Set(value); }
        public float? Coin { get => Get<float?>(); set => Set(value); }
        public DateTime? LastLoginDate { get => Get<DateTime?>(); set => Set(value); }
        public LoginResponseType? LoginResponseType { get => Get<LoginResponseType?>(); set => Set(value); }
        public string Locale { get => Get<string>(); set => Set(value); }
        public bool IsClientLogin { get => Get<bool>(); set => Set(value); }

        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Player>()
                .HasIndex(b => b.Token);
        }
    }
}
