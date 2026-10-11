using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconstructNodesServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Ability",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    AbilitySettingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    Category = table.Column<int>(type: "int", nullable: true),
                    Weights = table.Column<float>(type: "real", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Level = table.Column<float>(type: "real", nullable: true),
                    MaxLevel = table.Column<float>(type: "real", nullable: true),
                    CurrentExp = table.Column<float>(type: "real", nullable: true),
                    IsOwned = table.Column<bool>(type: "bit", nullable: true),
                    Grade = table.Column<float>(type: "real", nullable: true),
                    Attack = table.Column<float>(type: "real", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ability", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbilityCard",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    AbilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AbilityCardSettingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    AbilitySettingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UsageType = table.Column<int>(type: "int", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Weights = table.Column<float>(type: "real", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Level = table.Column<float>(type: "real", nullable: true),
                    MaxLevel = table.Column<float>(type: "real", nullable: true),
                    CurrentExp = table.Column<float>(type: "real", nullable: true),
                    IsOwned = table.Column<bool>(type: "bit", nullable: true),
                    Grade = table.Column<float>(type: "real", nullable: true),
                    Attack = table.Column<float>(type: "real", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbilityCard", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbilityCardSetting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    AbilitySettingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UsageType = table.Column<int>(type: "int", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Weights = table.Column<float>(type: "real", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Level = table.Column<float>(type: "real", nullable: true),
                    MaxLevel = table.Column<float>(type: "real", nullable: true),
                    CurrentExp = table.Column<float>(type: "real", nullable: true),
                    IsOwned = table.Column<bool>(type: "bit", nullable: true),
                    Grade = table.Column<float>(type: "real", nullable: true),
                    Attack = table.Column<float>(type: "real", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbilityCardSetting", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbilityProficiencySetting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false, comment: "(0) (1) (2) (3) (4) (5) (6) (7) (8) (9) (10)"),
                    SpritePath = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbilityProficiencySetting", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbilitySelect",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AbilityCategoryArray = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbilitySelect", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbilitySetting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    Category = table.Column<int>(type: "int", nullable: true),
                    Weights = table.Column<float>(type: "real", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Level = table.Column<float>(type: "real", nullable: true),
                    MaxLevel = table.Column<float>(type: "real", nullable: true),
                    CurrentExp = table.Column<float>(type: "real", nullable: true),
                    IsOwned = table.Column<bool>(type: "bit", nullable: true),
                    Grade = table.Column<float>(type: "real", nullable: true),
                    Attack = table.Column<float>(type: "real", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbilitySetting", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Achievement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    AchievementSettingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Total = table.Column<float>(type: "real", nullable: true),
                    DayTotal = table.Column<float>(type: "real", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Achievement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AchievementDetail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    AchievementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AchievementDetailSettingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    AchievementSettingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Target = table.Column<int>(type: "int", nullable: false),
                    IsReward = table.Column<bool>(type: "bit", nullable: false),
                    RewardDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GainCoins = table.Column<float>(type: "real", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: true),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AchievementDetail", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AchievementDetailSetting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    AchievementSettingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Target = table.Column<int>(type: "int", nullable: false),
                    IsReward = table.Column<bool>(type: "bit", nullable: false),
                    RewardDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GainCoins = table.Column<float>(type: "real", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: true),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AchievementDetailSetting", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AchievementSetting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AchievementSetting", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Character",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Category = table.Column<int>(type: "int", nullable: false, comment: "(0) (1) (2) (3) (4)"),
                    Level = table.Column<float>(type: "real", nullable: true),
                    CurrentExp = table.Column<float>(type: "real", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Character", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailVerify",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VerificationCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VerificationDeadline = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsVerification = table.Column<bool>(type: "bit", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailVerify", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Equipment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Category = table.Column<int>(type: "int", nullable: false, comment: "(1) (2) (3) (4) (5) (6) (7) (8) (9) (10) (11) (12) (13) (14) (15) (16) (17) (18) (19) (20) (21) (22) (23) (24) (25) (26) (27) (28) (29) (30) (31) (32) (33) (34) (35) (36) (37) (38) (39) (40) (41) (42) (43) (44) (45)"),
                    IsEquipped = table.Column<bool>(type: "bit", nullable: false),
                    IsLock = table.Column<bool>(type: "bit", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameCollection",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Type = table.Column<int>(type: "int", nullable: false, comment: "(1) (2) (3) (4) (5) (6) (7) (8)"),
                    Count = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameCollection", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManyToMany",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    SourceTable = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetTable = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelationType = table.Column<int>(type: "int", nullable: true),
                    Params = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManyToMany", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Map",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    MapSettingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SpritePath = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Map", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MapSetting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SpritePath = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapSetting", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Modifier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Type = table.Column<int>(type: "int", nullable: false, comment: "(1) (2) (3) (4) (5) (6) (7) (8) (9) (10) (11) (12) (13) (14) (15) (16) (17)"),
                    Value = table.Column<float>(type: "real", nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Modifier", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonsterSetting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SpritePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Hp = table.Column<float>(type: "real", nullable: true),
                    Movespeed = table.Column<float>(type: "real", nullable: true),
                    Attack = table.Column<float>(type: "real", nullable: true),
                    Recovery = table.Column<float>(type: "real", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false, comment: "(0) (1) (2) (3) (4) (5)")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonsterSetting", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Player",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Account = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Password = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Token = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GoogleId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmailVerifyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NationalId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InventorySlotCount = table.Column<int>(type: "int", nullable: true),
                    Coin = table.Column<float>(type: "real", nullable: true),
                    LastLoginDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LoginResponseType = table.Column<int>(type: "int", nullable: true),
                    Locale = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsClientLogin = table.Column<bool>(type: "bit", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Player", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerParams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Group = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerParams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SettingVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    TableName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingVersion", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemConfig",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Group = table.Column<int>(type: "int", nullable: false, comment: "(0) (1) (2)"),
                    Key = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemConfig", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerSave",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaveKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerSave", x => new { x.PlayerId, x.SaveKey });
                    table.ForeignKey(
                        name: "FK_PlayerSave_Player_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Player",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ability_CreateUserId",
                table: "Ability",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AbilityCard_AbilityId",
                table: "AbilityCard",
                column: "AbilityId");

            migrationBuilder.CreateIndex(
                name: "IX_AbilityCard_CreateUserId",
                table: "AbilityCard",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AbilityCardSetting_CreateUserId",
                table: "AbilityCardSetting",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AbilityProficiencySetting_CreateUserId",
                table: "AbilityProficiencySetting",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AbilitySelect_CreateUserId",
                table: "AbilitySelect",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AbilitySetting_CreateUserId",
                table: "AbilitySetting",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Achievement_CreateUserId",
                table: "Achievement",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementDetail_AchievementId",
                table: "AchievementDetail",
                column: "AchievementId");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementDetail_CreateUserId",
                table: "AchievementDetail",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementDetailSetting_CreateUserId",
                table: "AchievementDetailSetting",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementSetting_CreateUserId",
                table: "AchievementSetting",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Character_CreateUserId",
                table: "Character",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerify_CreateUserId",
                table: "EmailVerify",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_CreateUserId",
                table: "Equipment",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameCollection_CreateUserId",
                table: "GameCollection",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ManyToMany_CreateUserId",
                table: "ManyToMany",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ManyToMany_SourceId",
                table: "ManyToMany",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ManyToMany_TargetId",
                table: "ManyToMany",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_ManyToMany_TargetTable",
                table: "ManyToMany",
                column: "TargetTable");

            migrationBuilder.CreateIndex(
                name: "IX_Map_CreateUserId",
                table: "Map",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MapSetting_CreateUserId",
                table: "MapSetting",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Modifier_CreateUserId",
                table: "Modifier",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MonsterSetting_CreateUserId",
                table: "MonsterSetting",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Player_CreateUserId",
                table: "Player",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Player_Token",
                table: "Player",
                column: "Token");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerParams_CreateUserId",
                table: "PlayerParams",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SettingVersion_CreateUserId",
                table: "SettingVersion",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemConfig_CreateUserId",
                table: "SystemConfig",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemConfig_Group",
                table: "SystemConfig",
                column: "Group");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Ability");

            migrationBuilder.DropTable(
                name: "AbilityCard");

            migrationBuilder.DropTable(
                name: "AbilityCardSetting");

            migrationBuilder.DropTable(
                name: "AbilityProficiencySetting");

            migrationBuilder.DropTable(
                name: "AbilitySelect");

            migrationBuilder.DropTable(
                name: "AbilitySetting");

            migrationBuilder.DropTable(
                name: "Achievement");

            migrationBuilder.DropTable(
                name: "AchievementDetail");

            migrationBuilder.DropTable(
                name: "AchievementDetailSetting");

            migrationBuilder.DropTable(
                name: "AchievementSetting");

            migrationBuilder.DropTable(
                name: "Character");

            migrationBuilder.DropTable(
                name: "EmailVerify");

            migrationBuilder.DropTable(
                name: "Equipment");

            migrationBuilder.DropTable(
                name: "GameCollection");

            migrationBuilder.DropTable(
                name: "ManyToMany");

            migrationBuilder.DropTable(
                name: "Map");

            migrationBuilder.DropTable(
                name: "MapSetting");

            migrationBuilder.DropTable(
                name: "Modifier");

            migrationBuilder.DropTable(
                name: "MonsterSetting");

            migrationBuilder.DropTable(
                name: "PlayerParams");

            migrationBuilder.DropTable(
                name: "PlayerSave");

            migrationBuilder.DropTable(
                name: "SettingVersion");

            migrationBuilder.DropTable(
                name: "SystemConfig");

            migrationBuilder.DropTable(
                name: "Player");
        }
    }
}
