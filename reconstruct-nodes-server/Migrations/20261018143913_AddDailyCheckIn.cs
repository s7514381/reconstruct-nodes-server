using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconstructNodesServer.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyCheckIn",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    DateKey = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyCheckIn", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyCheckIn_CreateUserId",
                table: "DailyCheckIn",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyCheckIn_CreateUserId_DateKey",
                table: "DailyCheckIn",
                columns: new[] { "CreateUserId", "DateKey" },
                unique: true,
                filter: "[Status] <> -1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyCheckIn");
        }
    }
}
