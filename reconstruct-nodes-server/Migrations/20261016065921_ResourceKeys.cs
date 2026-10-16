using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconstructNodesServer.Migrations
{
    /// <inheritdoc />
    public partial class ResourceKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM PlayerParams WHERE DATALENGTH([Group]) > 400 OR DATALENGTH(Name) > 900)
                    THROW 51004, 'Resource key exceeds the bounded index length', 1;
                IF EXISTS (SELECT 1 FROM PlayerParams WHERE Status != -1 AND CreateUserId IS NOT NULL
                    GROUP BY CreateUserId, [Group], Name HAVING COUNT(*) > 1)
                    THROW 51004, 'Duplicate active resource keys require explicit resolution', 1;
                """);
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "PlayerParams",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Group",
                table: "PlayerParams",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerParams_CreateUserId_Group_Name",
                table: "PlayerParams",
                columns: new[] { "CreateUserId", "Group", "Name" },
                unique: true,
                filter: "[Status] <> -1 AND [CreateUserId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerParams_CreateUserId_Group_Name",
                table: "PlayerParams");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "PlayerParams",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Group",
                table: "PlayerParams",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
