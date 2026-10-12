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

            // Google 玩家唯一來源：同一 GoogleId 只能有一筆非訪客列。訪客列（IsClientLogin = 1）不納入，
            // 以免舊版 Bind 殘留的 GoogleId 阻擋 Google 玩家建立。GoogleId 需可作為索引鍵，故限長 128（Google sub 實際為數字串）。
            modelBuilder.Entity<Player>()
                .Property(b => b.GoogleId)
                .HasMaxLength(128);

            modelBuilder.Entity<Player>()
                .HasIndex(b => b.GoogleId)
                .IsUnique()
                .HasFilter("[GoogleId] IS NOT NULL AND [IsClientLogin] = 0")
                .HasDatabaseName("UX_Player_GoogleId_NonClient");
        }
    }
}
