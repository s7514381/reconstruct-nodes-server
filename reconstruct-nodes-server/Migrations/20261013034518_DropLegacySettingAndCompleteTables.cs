using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconstructNodesServer.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacySettingAndCompleteTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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
                name: "AchievementDetailSetting");

            migrationBuilder.DropTable(
                name: "AchievementSetting");

            migrationBuilder.DropTable(
                name: "Character");

            migrationBuilder.DropTable(
                name: "Equipment");

            migrationBuilder.DropTable(
                name: "GameCollection");

            migrationBuilder.DropTable(
                name: "Map");

            migrationBuilder.DropTable(
                name: "MapSetting");

            migrationBuilder.DropTable(
                name: "Modifier");

            migrationBuilder.DropTable(
                name: "MonsterSetting");

            migrationBuilder.DropTable(
                name: "SettingVersion");

            migrationBuilder.DropColumn(
                name: "AchievementDetailSettingId",
                table: "AchievementDetail");

            migrationBuilder.DropColumn(
                name: "AchievementSettingId",
                table: "AchievementDetail");

            migrationBuilder.DropColumn(
                name: "AchievementSettingId",
                table: "Achievement");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AchievementDetailSettingId",
                table: "AchievementDetail",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AchievementSettingId",
                table: "AchievementDetail",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AchievementSettingId",
                table: "Achievement",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Ability",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    AbilitySettingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Attack = table.Column<float>(type: "real", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentExp = table.Column<float>(type: "real", nullable: true),
                    Grade = table.Column<float>(type: "real", nullable: true),
                    IsOwned = table.Column<bool>(type: "bit", nullable: true),
                    Level = table.Column<float>(type: "real", nullable: true),
                    MaxLevel = table.Column<float>(type: "real", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    Weights = table.Column<float>(type: "real", nullable: true)
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
                    AbilityCardSettingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AbilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AbilitySettingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Attack = table.Column<float>(type: "real", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentExp = table.Column<float>(type: "real", nullable: true),
                    Grade = table.Column<float>(type: "real", nullable: true),
                    IsOwned = table.Column<bool>(type: "bit", nullable: true),
                    Level = table.Column<float>(type: "real", nullable: true),
                    MaxLevel = table.Column<float>(type: "real", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    UsageType = table.Column<int>(type: "int", nullable: true),
                    Weights = table.Column<float>(type: "real", nullable: true)
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
                    AbilitySettingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Attack = table.Column<float>(type: "real", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentExp = table.Column<float>(type: "real", nullable: true),
                    Grade = table.Column<float>(type: "real", nullable: true),
                    IsOwned = table.Column<bool>(type: "bit", nullable: true),
                    Level = table.Column<float>(type: "real", nullable: true),
                    MaxLevel = table.Column<float>(type: "real", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    UsageType = table.Column<int>(type: "int", nullable: true),
                    Weights = table.Column<float>(type: "real", nullable: true)
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
                    Category = table.Column<int>(type: "int", nullable: false, comment: "(0) (1) (2) (3) (4) (5) (6) (7) (8) (9) (10)"),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    SpritePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
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
                    AbilityCategoryArray = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
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
                    Attack = table.Column<float>(type: "real", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentExp = table.Column<float>(type: "real", nullable: true),
                    Grade = table.Column<float>(type: "real", nullable: true),
                    IsOwned = table.Column<bool>(type: "bit", nullable: true),
                    Level = table.Column<float>(type: "real", nullable: true),
                    MaxLevel = table.Column<float>(type: "real", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    Weights = table.Column<float>(type: "real", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbilitySetting", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AchievementDetailSetting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    AchievementSettingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GainCoins = table.Column<float>(type: "real", nullable: true),
                    IsReward = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RewardDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    Target = table.Column<int>(type: "int", nullable: false),
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
                    Category = table.Column<int>(type: "int", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
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
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentExp = table.Column<float>(type: "real", nullable: true),
                    Level = table.Column<float>(type: "real", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Character", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Equipment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Category = table.Column<int>(type: "int", nullable: false, comment: "(1) (2) (3) (4) (5) (6) (7) (8) (9) (10) (11) (12) (13) (14) (15) (16) (17) (18) (19) (20) (21) (22) (23) (24) (25) (26) (27) (28) (29) (30) (31) (32) (33) (34) (35) (36) (37) (38) (39) (40) (41) (42) (43) (44) (45)"),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsEquipped = table.Column<bool>(type: "bit", nullable: false),
                    IsLock = table.Column<bool>(type: "bit", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
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
                    Count = table.Column<int>(type: "int", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameCollection", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Map",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MapSettingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    SpritePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
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
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    SpritePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
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
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false, comment: "(1) (2) (3) (4) (5) (6) (7) (8) (9) (10) (11) (12) (13) (14) (15) (16) (17)"),
                    Value = table.Column<float>(type: "real", nullable: false)
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
                    Attack = table.Column<float>(type: "real", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false, comment: "(0) (1) (2) (3) (4) (5)"),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Hp = table.Column<float>(type: "real", nullable: true),
                    Movespeed = table.Column<float>(type: "real", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Recovery = table.Column<float>(type: "real", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    SpritePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonsterSetting", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SettingVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1"),
                    TableName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingVersion", x => x.Id);
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
                name: "IX_Equipment_CreateUserId",
                table: "Equipment",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameCollection_CreateUserId",
                table: "GameCollection",
                column: "CreateUserId");

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
                name: "IX_SettingVersion_CreateUserId",
                table: "SettingVersion",
                column: "CreateUserId");
        }
    }
}
