using Microsoft.EntityFrameworkCore;
using ReconstructNodesServer.Library.DBModel;
using ReconstructNodesServer.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReconstructNodesServer.Library.Models;

namespace ReconstructNodesServer
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public virtual DbSet<Equipment> Equipment { get; set; }
        public virtual DbSet<Modifier> Modifier { get; set; }
        public virtual DbSet<Character> Character { get; set; }
        public virtual DbSet<Player> Player { get; set; }
        public virtual DbSet<Ability> Ability { get; set; }
        public virtual DbSet<AbilityCard> AbilityCard { get; set; }
        public virtual DbSet<AbilitySetting> AbilitySetting { get; set; }
        public virtual DbSet<AbilityCardSetting> AbilityCardSetting { get; set; }
        public virtual DbSet<AbilityProficiencySetting> AbilityProficiencySetting { get; set; }
        public virtual DbSet<Map> Map { get; set; }
        public virtual DbSet<MapSetting> MapSetting { get; set; }
        public virtual DbSet<SettingVersion> SettingVersion { get; set; }
        public virtual DbSet<Achievement> Achievement { get; set; }
        public virtual DbSet<AchievementSetting> AchievementSetting { get; set; }
        public virtual DbSet<AchievementDetail> AchievementDetail { get; set; }
        public virtual DbSet<AchievementDetailSetting> AchievementDetailSetting { get; set; }
        public virtual DbSet<AchievementCollected> AchievementCollected { get; set; }
        public virtual DbSet<GameCollection> GameCollection { get; set; }
        public virtual DbSet<EmailVerify> EmailVerify { get; set; }
        public virtual DbSet<SystemConfig> SystemConfig { get; set; }
        public virtual DbSet<ManyToMany> ManyToMany { get; set; }
        public virtual DbSet<AbilitySelect> AbilitySelect { get; set; }
        public virtual DbSet<MonsterSetting> MonsterSetting { get; set; }
        public virtual DbSet<PlayerParams> PlayerParams { get; set; }
        public virtual DbSet<Item> Item { get; set; }
        public virtual DbSet<Weapon> Weapon { get; set; }
        public virtual DbSet<WeaponAffix> WeaponAffix { get; set; }
        public virtual DbSet<WeaponAffixValue> WeaponAffixValue { get; set; }
        public virtual DbSet<WeaponUniqueValue> WeaponUniqueValue { get; set; }
        public virtual DbSet<Deployment> Deployment { get; set; }
        public virtual DbSet<DeploymentSkill> DeploymentSkill { get; set; }
        public virtual DbSet<DeploymentSkillNode> DeploymentSkillNode { get; set; }
        public virtual DbSet<BattlefieldRoad> BattlefieldRoad { get; set; }
        public virtual DbSet<CharacterWeapon> CharacterWeapon { get; set; }
        public virtual DbSet<Talent> Talent { get; set; }
        public virtual DbSet<TalentAcquired> TalentAcquired { get; set; }
        public virtual DbSet<TalentLegacyCategory> TalentLegacyCategory { get; set; }
        //

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            foreach (var tableType in CommentGenerator.GetTableTypes(this.GetType()))
            {
                if (CommentGenerator.NeedGenerateComment(tableType, out string tableComment))
                {
                    modelBuilder.Entity(tableType).Metadata.SetAnnotation("Relational:Comment", tableComment);
                }

                foreach (var field in tableType.GetProperties())
                {
                    if (CommentGenerator.NeedGenerateComment(field, out string comment))
                    {
                        modelBuilder.Entity(tableType).Property(field.Name).HasAnnotation("Relational:Comment", comment);
                    }
                }
            }

            //尋覽DbSet裡面的Table
            foreach (var setProperty in this.GetType().GetProperties()
                .Where(p => p.PropertyType.IsGenericType &&
                            p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)))
            {
                var entityType = setProperty.PropertyType.GetGenericArguments()[0];

                // 為 Id 屬性設定 NEWID() 預設值 (假設是 GUID 類型)
                var idProperty = entityType.GetProperty("Id");
                if (idProperty != null && idProperty.PropertyType == typeof(Guid))
                {
                    modelBuilder.Entity(entityType)
                        .Property("Id")
                        .HasDefaultValueSql("NEWID()");
                }

                if (entityType.GetProperty("Status") != null)
                {
                    modelBuilder.Entity(entityType)
                        .Property("Status")
                        .HasDefaultValueSql("1");
                }

                //在每個IdModelBase去覆寫OnModelCreating來設定額外的ModelBuilder設定(請參考ManyToMany)
                if (typeof(ModelBase).IsAssignableFrom(entityType))
                {
                    try
                    {
                        var instance = Activator.CreateInstance(entityType) as ModelBase;
                        if (instance != null)
                        {
                            instance.OnModelCreating(modelBuilder);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"無法建立 {entityType.Name} 的實例: {ex.Message}");
                    }
                }
            }

            // 唯一索引需排除已軟刪除（Status = -1）的列，HasFilter 為 SQL Server 專用 API，故設定於此而非 Library 實體。
            modelBuilder.Entity<Item>()
                .HasIndex(b => new { b.CreateUserId, b.ItemKey })
                .IsUnique()
                .HasFilter("[Status] <> -1");

            modelBuilder.Entity<Weapon>()
                .HasIndex(b => new { b.CreateUserId, b.EntryKey })
                .IsUnique()
                .HasFilter("[Status] <> -1");

            // 角色裝備每位玩家每個角色只有一列，以唯一索引保證併發寫入時不會產生重複列。
            modelBuilder.Entity<CharacterWeapon>()
                .HasIndex(b => new { b.CreateUserId, b.CharacterKind })
                .IsUnique()
                .HasFilter("[Status] <> -1");

            // 天賦每位玩家只有一列父資料，以唯一索引保證併發寫入時不會產生重複列。
            modelBuilder.Entity<Talent>()
                .HasIndex(b => b.CreateUserId)
                .IsUnique()
                .HasFilter("[Status] <> -1");

            base.OnModelCreating(modelBuilder);
        }
    }
}
