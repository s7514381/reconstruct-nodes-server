using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconstructNodesServer.Migrations
{
    /// <inheritdoc />
    public partial class AddAdRewardGrant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdRewardGrant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    TransactionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Claimed = table.Column<bool>(type: "bit", nullable: false),
                    DateKey = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Slot = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdRewardGrant", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdRewardGrant_CreateUserId",
                table: "AdRewardGrant",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AdRewardGrant_CreateUserId_DateKey_Slot",
                table: "AdRewardGrant",
                columns: new[] { "CreateUserId", "DateKey", "Slot" },
                unique: true,
                filter: "[Status] <> -1 AND [Slot] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AdRewardGrant_CreateUserId_RequestId",
                table: "AdRewardGrant",
                columns: new[] { "CreateUserId", "RequestId" },
                unique: true,
                filter: "[Status] <> -1");

            migrationBuilder.CreateIndex(
                name: "IX_AdRewardGrant_TransactionId",
                table: "AdRewardGrant",
                column: "TransactionId",
                unique: true,
                filter: "[Status] <> -1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdRewardGrant");
        }
    }
}
