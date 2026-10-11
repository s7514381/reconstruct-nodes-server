using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconstructNodesServer.Migrations
{
    /// <inheritdoc />
    public partial class SplitSavesIntoDomainTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerSave");

            migrationBuilder.CreateTable(
                name: "AchievementCollected",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    AchievementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AchievementCollected", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Wave = table.Column<int>(type: "int", nullable: false),
                    Gold = table.Column<long>(type: "bigint", nullable: false),
                    MaxWaveReached = table.Column<int>(type: "int", nullable: false),
                    Difficulty = table.Column<int>(type: "int", nullable: false),
                    LifetimeEnemiesDefeated = table.Column<long>(type: "bigint", nullable: false),
                    InfiniteLevel = table.Column<int>(type: "int", nullable: false),
                    WaveCompleted = table.Column<bool>(type: "bit", nullable: false),
                    BattlefieldVersion = table.Column<int>(type: "int", nullable: true),
                    BattlefieldColumns = table.Column<int>(type: "int", nullable: true),
                    BattlefieldRows = table.Column<int>(type: "int", nullable: true),
                    CastleX = table.Column<int>(type: "int", nullable: true),
                    CastleY = table.Column<int>(type: "int", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProgress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameProgressBattlefieldRoad",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    GameProgressId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    X = table.Column<int>(type: "int", nullable: false),
                    Y = table.Column<int>(type: "int", nullable: false),
                    Corner = table.Column<bool>(type: "bit", nullable: false),
                    Rotation = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProgressBattlefieldRoad", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameProgressDeployment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    GameProgressId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SpotIndex = table.Column<int>(type: "int", nullable: false),
                    CellX = table.Column<int>(type: "int", nullable: true),
                    CellY = table.Column<int>(type: "int", nullable: true),
                    CharacterName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Wave = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProgressDeployment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameProgressDeploymentSkill",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    GameProgressDeploymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProgressDeploymentSkill", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameProgressDeploymentSkillNode",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    GameProgressDeploymentSkillId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NodeType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LootId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProgressDeploymentSkillNode", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameProgressSetting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    GameProgressId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SettingGroup = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SettingKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProgressSetting", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameProgressWeapon",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    GameProgressId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    ScopeKey = table.Column<int>(type: "int", nullable: false),
                    EntryKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WeaponKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rarity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QualityVersion = table.Column<int>(type: "int", nullable: true),
                    UniqueKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProgressWeapon", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameProgressWeaponAffix",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    GameProgressWeaponId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AffixKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rolled = table.Column<bool>(type: "bit", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProgressWeaponAffix", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameProgressWeaponAffixValue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    GameProgressWeaponAffixId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ValueKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProgressWeaponAffixValue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameProgressWeaponUniqueValue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    GameProgressWeaponId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ValueKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProgressWeaponUniqueValue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Item",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    ItemKey = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Item", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Talent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    TalentPoints = table.Column<long>(type: "bigint", nullable: false),
                    TotalSpentPoints = table.Column<long>(type: "bigint", nullable: false),
                    PointsRecalculationVersion = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Talent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TalentAcquired",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    TalentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TreeId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NodeId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalentAcquired", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TalentLegacyCategory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    TalentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TreeId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NodeId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalentLegacyCategory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Weapon",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    EntryKey = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    WeaponKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WeaponFamilyKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rarity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UniqueKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Weapon", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeaponAffix",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    WeaponId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AffixKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rolled = table.Column<bool>(type: "bit", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeaponAffix", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeaponAffixValue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    WeaponAffixId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ValueKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeaponAffixValue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeaponUniqueValue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    WeaponId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ValueKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeaponUniqueValue", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AchievementCollected_AchievementId",
                table: "AchievementCollected",
                column: "AchievementId");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementCollected_CreateUserId",
                table: "AchievementCollected",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgress_CreateUserId",
                table: "GameProgress",
                column: "CreateUserId",
                unique: true,
                filter: "[Status] <> -1");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressBattlefieldRoad_CreateUserId",
                table: "GameProgressBattlefieldRoad",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressBattlefieldRoad_GameProgressId",
                table: "GameProgressBattlefieldRoad",
                column: "GameProgressId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressDeployment_CreateUserId",
                table: "GameProgressDeployment",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressDeployment_GameProgressId",
                table: "GameProgressDeployment",
                column: "GameProgressId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressDeploymentSkill_CreateUserId",
                table: "GameProgressDeploymentSkill",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressDeploymentSkill_GameProgressDeploymentId",
                table: "GameProgressDeploymentSkill",
                column: "GameProgressDeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressDeploymentSkillNode_CreateUserId",
                table: "GameProgressDeploymentSkillNode",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressDeploymentSkillNode_GameProgressDeploymentSkillId",
                table: "GameProgressDeploymentSkillNode",
                column: "GameProgressDeploymentSkillId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressSetting_CreateUserId",
                table: "GameProgressSetting",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressSetting_GameProgressId",
                table: "GameProgressSetting",
                column: "GameProgressId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressWeapon_CreateUserId",
                table: "GameProgressWeapon",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressWeapon_GameProgressId",
                table: "GameProgressWeapon",
                column: "GameProgressId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressWeaponAffix_CreateUserId",
                table: "GameProgressWeaponAffix",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressWeaponAffix_GameProgressWeaponId",
                table: "GameProgressWeaponAffix",
                column: "GameProgressWeaponId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressWeaponAffixValue_CreateUserId",
                table: "GameProgressWeaponAffixValue",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressWeaponAffixValue_GameProgressWeaponAffixId",
                table: "GameProgressWeaponAffixValue",
                column: "GameProgressWeaponAffixId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressWeaponUniqueValue_CreateUserId",
                table: "GameProgressWeaponUniqueValue",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProgressWeaponUniqueValue_GameProgressWeaponId",
                table: "GameProgressWeaponUniqueValue",
                column: "GameProgressWeaponId");

            migrationBuilder.CreateIndex(
                name: "IX_Item_CreateUserId",
                table: "Item",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Item_CreateUserId_ItemKey",
                table: "Item",
                columns: new[] { "CreateUserId", "ItemKey" },
                unique: true,
                filter: "[Status] <> -1");

            migrationBuilder.CreateIndex(
                name: "IX_Talent_CreateUserId",
                table: "Talent",
                column: "CreateUserId",
                unique: true,
                filter: "[Status] <> -1");

            migrationBuilder.CreateIndex(
                name: "IX_TalentAcquired_CreateUserId",
                table: "TalentAcquired",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TalentAcquired_TalentId",
                table: "TalentAcquired",
                column: "TalentId");

            migrationBuilder.CreateIndex(
                name: "IX_TalentLegacyCategory_CreateUserId",
                table: "TalentLegacyCategory",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TalentLegacyCategory_TalentId",
                table: "TalentLegacyCategory",
                column: "TalentId");

            migrationBuilder.CreateIndex(
                name: "IX_Weapon_CreateUserId",
                table: "Weapon",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Weapon_CreateUserId_EntryKey",
                table: "Weapon",
                columns: new[] { "CreateUserId", "EntryKey" },
                unique: true,
                filter: "[Status] <> -1");

            migrationBuilder.CreateIndex(
                name: "IX_WeaponAffix_CreateUserId",
                table: "WeaponAffix",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WeaponAffix_WeaponId",
                table: "WeaponAffix",
                column: "WeaponId");

            migrationBuilder.CreateIndex(
                name: "IX_WeaponAffixValue_CreateUserId",
                table: "WeaponAffixValue",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WeaponAffixValue_WeaponAffixId",
                table: "WeaponAffixValue",
                column: "WeaponAffixId");

            migrationBuilder.CreateIndex(
                name: "IX_WeaponUniqueValue_CreateUserId",
                table: "WeaponUniqueValue",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WeaponUniqueValue_WeaponId",
                table: "WeaponUniqueValue",
                column: "WeaponId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AchievementCollected");

            migrationBuilder.DropTable(
                name: "GameProgress");

            migrationBuilder.DropTable(
                name: "GameProgressBattlefieldRoad");

            migrationBuilder.DropTable(
                name: "GameProgressDeployment");

            migrationBuilder.DropTable(
                name: "GameProgressDeploymentSkill");

            migrationBuilder.DropTable(
                name: "GameProgressDeploymentSkillNode");

            migrationBuilder.DropTable(
                name: "GameProgressSetting");

            migrationBuilder.DropTable(
                name: "GameProgressWeapon");

            migrationBuilder.DropTable(
                name: "GameProgressWeaponAffix");

            migrationBuilder.DropTable(
                name: "GameProgressWeaponAffixValue");

            migrationBuilder.DropTable(
                name: "GameProgressWeaponUniqueValue");

            migrationBuilder.DropTable(
                name: "Item");

            migrationBuilder.DropTable(
                name: "Talent");

            migrationBuilder.DropTable(
                name: "TalentAcquired");

            migrationBuilder.DropTable(
                name: "TalentLegacyCategory");

            migrationBuilder.DropTable(
                name: "Weapon");

            migrationBuilder.DropTable(
                name: "WeaponAffix");

            migrationBuilder.DropTable(
                name: "WeaponAffixValue");

            migrationBuilder.DropTable(
                name: "WeaponUniqueValue");

            migrationBuilder.CreateTable(
                name: "PlayerSave",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaveKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
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
        }
    }
}
