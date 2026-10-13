using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconstructNodesServer.Migrations
{
    /// <inheritdoc />
    public partial class SeparateCharactersFromDeployments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 舊部署沒有可對應的穩定角色 Id，不從歷史配置推導擁有角色。
            migrationBuilder.Sql("DELETE FROM [Deployment];");
            migrationBuilder.Sql("DELETE FROM [Item] WHERE [Category] = 'character';");

            migrationBuilder.DropTable(
                name: "CharacterWeapon");

            migrationBuilder.DropTable(
                name: "DeploymentSkill");

            migrationBuilder.DropTable(
                name: "DeploymentSkillNode");

            migrationBuilder.DropColumn(
                name: "CharacterName",
                table: "Deployment");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Deployment");

            migrationBuilder.DropColumn(
                name: "WeaponEntryKey",
                table: "Deployment");

            migrationBuilder.DropColumn(
                name: "WeaponQualityVersion",
                table: "Deployment");

            migrationBuilder.AddColumn<Guid>(
                name: "CharacterId",
                table: "Deployment",
                type: "uniqueidentifier",
                nullable: false);

            migrationBuilder.CreateTable(
                name: "Character",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WeaponEntryKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    WeaponQualityVersion = table.Column<int>(type: "int", nullable: true),
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
                name: "CharacterSkillNode",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    CharacterSkillTreeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_CharacterSkillNode", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CharacterSkillTree",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterSkillTree", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deployment_CharacterId",
                table: "Deployment",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_Character_CreateUserId",
                table: "Character",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Character_CreateUserId_Kind",
                table: "Character",
                columns: new[] { "CreateUserId", "Kind" },
                unique: true,
                filter: "[Status] <> -1");

            migrationBuilder.CreateIndex(
                name: "IX_Character_CreateUserId_WeaponEntryKey",
                table: "Character",
                columns: new[] { "CreateUserId", "WeaponEntryKey" },
                unique: true,
                filter: "[Status] <> -1 AND [WeaponEntryKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterSkillNode_CharacterSkillTreeId",
                table: "CharacterSkillNode",
                column: "CharacterSkillTreeId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterSkillNode_CreateUserId",
                table: "CharacterSkillNode",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterSkillTree_CharacterId",
                table: "CharacterSkillTree",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterSkillTree_CreateUserId",
                table: "CharacterSkillTree",
                column: "CreateUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 回退僅恢復 schema，角色引用不能轉回原始部署快照。
            migrationBuilder.Sql("DELETE FROM [Deployment];");

            migrationBuilder.DropTable(
                name: "Character");

            migrationBuilder.DropTable(
                name: "CharacterSkillNode");

            migrationBuilder.DropTable(
                name: "CharacterSkillTree");

            migrationBuilder.DropIndex(
                name: "IX_Deployment_CharacterId",
                table: "Deployment");

            migrationBuilder.DropColumn(
                name: "CharacterId",
                table: "Deployment");

            migrationBuilder.AddColumn<string>(
                name: "CharacterName",
                table: "Deployment",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Deployment",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WeaponEntryKey",
                table: "Deployment",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeaponQualityVersion",
                table: "Deployment",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CharacterWeapon",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    CharacterKind = table.Column<int>(type: "int", nullable: false),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntryKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterWeapon", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentSkill",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeploymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentSkill", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentSkillNode",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeploymentSkillId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: true),
                    LootId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NodeType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentSkillNode", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterWeapon_CreateUserId",
                table: "CharacterWeapon",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterWeapon_CreateUserId_CharacterKind",
                table: "CharacterWeapon",
                columns: new[] { "CreateUserId", "CharacterKind" },
                unique: true,
                filter: "[Status] <> -1");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentSkill_CreateUserId",
                table: "DeploymentSkill",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentSkill_DeploymentId",
                table: "DeploymentSkill",
                column: "DeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentSkillNode_CreateUserId",
                table: "DeploymentSkillNode",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentSkillNode_DeploymentSkillId",
                table: "DeploymentSkillNode",
                column: "DeploymentSkillId");
        }
    }
}
