using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconstructNodesServer.Migrations
{
    /// <inheritdoc />
    public partial class CharacterInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Character_CreateUserId_Kind",
                table: "Character");

            migrationBuilder.AddColumn<int>(
                name: "Fate",
                table: "Character",
                type: "int",
                nullable: false,
                defaultValue: 0,
                comment: "(0) (1) (2) (3) (4) (5) (6) (7) (8) (9)");

            migrationBuilder.AddColumn<int>(
                name: "Level",
                table: "Character",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Personality",
                table: "Character",
                type: "int",
                nullable: false,
                defaultValue: 0,
                comment: "(0) (1) (2) (3) (4) (5) (6) (7) (8) (9) (10) (11) (12) (13) (14) (15) (16) (17) (18) (19) (20) (21) (22) (23) (24)");

            migrationBuilder.CreateIndex(
                name: "IX_Character_CreateUserId_Kind",
                table: "Character",
                columns: new[] { "CreateUserId", "Kind" },
                unique: true,
                filter: "[Status] <> -1 AND [Kind] = 3");

            // 既有一般角色補上隨機性格／命格（核心沒有標籤，維持 0 由客戶端忽略）；等級已由欄位預設值設為 1。
            migrationBuilder.Sql("""
                UPDATE [Character]
                SET Personality = ABS(CHECKSUM(NEWID())) % 25, Fate = ABS(CHECKSUM(NEWID())) % 10
                WHERE Kind <> 3;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Character_CreateUserId_Kind",
                table: "Character");

            migrationBuilder.DropColumn(
                name: "Fate",
                table: "Character");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "Character");

            migrationBuilder.DropColumn(
                name: "Personality",
                table: "Character");

            migrationBuilder.CreateIndex(
                name: "IX_Character_CreateUserId_Kind",
                table: "Character",
                columns: new[] { "CreateUserId", "Kind" },
                unique: true,
                filter: "[Status] <> -1");
        }
    }
}
