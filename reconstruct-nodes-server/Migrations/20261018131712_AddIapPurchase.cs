using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReconstructNodesServer.Migrations
{
    /// <inheritdoc />
    public partial class AddIapPurchase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IapPurchase",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    PurchaseToken = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ProductId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OrderId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Diamonds = table.Column<int>(type: "int", nullable: false),
                    Consumed = table.Column<bool>(type: "bit", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: true, defaultValueSql: "1")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IapPurchase", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IapPurchase_CreateUserId",
                table: "IapPurchase",
                column: "CreateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IapPurchase_PurchaseToken",
                table: "IapPurchase",
                column: "PurchaseToken",
                unique: true,
                filter: "[Status] <> -1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IapPurchase");
        }
    }
}
