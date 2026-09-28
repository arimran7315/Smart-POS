using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPOS.Infrastructure.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddOperationalQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Sales_SoldAtUtc",
                table: "Sales",
                column: "SoldAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_PurchasedAtUtc",
                table: "Purchases",
                column: "PurchasedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_SoldAtUtc",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_PurchasedAtUtc",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Products_Name",
                table: "Products");
        }
    }
}
