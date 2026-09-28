using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPOS.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [Products] WHERE DATALENGTH([Name]) > 400) THROW 51000, N'Product names longer than 200 characters must be corrected before adding the product-name index.', 1;");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Products",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

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

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Products",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);
        }
    }
}
