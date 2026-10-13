using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconstructNodesServer.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStoredGameNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Name",
                table: "Item");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Character");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Item",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Character",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
