using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconstructNodesServer.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthoritativeGoldAndShopRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EconomyRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EconomyRequest", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EconomyRequest_CreateUserId",
                table: "EconomyRequest",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EconomyRequest_CreateUserId_RequestId",
                table: "EconomyRequest",
                columns: new[] { "CreateUserId", "RequestId" },
                unique: true,
                filter: "[CreateUserId] IS NOT NULL");

            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT CreateUserId FROM PlayerParams
                    WHERE [Group] = 'game_progress' AND Name = 'gold' AND Status <> -1
                    GROUP BY CreateUserId HAVING COUNT(*) > 1
                ) THROW 51000, 'Duplicate legacy gold rows; resolve before migrating.', 1;
                IF EXISTS (
                    SELECT 1 FROM PlayerParams
                    WHERE [Group] = 'game_progress' AND Name = 'gold' AND Status <> -1
                      AND (NULLIF(LTRIM(RTRIM(Value)), '') IS NULL
                           OR TRY_CONVERT(bigint, Value) IS NULL OR TRY_CONVERT(bigint, Value) < 0)
                ) THROW 51000, 'Invalid legacy gold balance; resolve before migrating.', 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EconomyRequest");

        }
    }
}
