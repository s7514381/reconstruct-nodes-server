using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconstructNodesServer.Migrations
{
    /// <inheritdoc />
    public partial class GooglePlayerGoogleIdUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 既有資料防呆：重複的非訪客 GoogleId 或超過 128 字元者會使遷移失敗並明示，不自動刪除或改寫玩家資料。
            // 需先人工合併/處理後再執行遷移。
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM Player WHERE GoogleId IS NOT NULL AND IsClientLogin = 0 GROUP BY GoogleId HAVING COUNT(*) > 1)
    THROW 50000, 'GooglePlayerGoogleIdUnique: Player 存在重複的非訪客 GoogleId，請先人工處理重複列後再遷移。', 1;
IF EXISTS (SELECT 1 FROM Player WHERE LEN(GoogleId) > 128)
    THROW 50000, 'GooglePlayerGoogleIdUnique: Player.GoogleId 超過 128 字元，無法建立唯一索引，請先人工處理。', 1;
");

            migrationBuilder.AlterColumn<string>(
                name: "GoogleId",
                table: "Player",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Player_GoogleId_NonClient",
                table: "Player",
                column: "GoogleId",
                unique: true,
                filter: "[GoogleId] IS NOT NULL AND [IsClientLogin] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Player_GoogleId_NonClient",
                table: "Player");

            migrationBuilder.AlterColumn<string>(
                name: "GoogleId",
                table: "Player",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);
        }
    }
}
