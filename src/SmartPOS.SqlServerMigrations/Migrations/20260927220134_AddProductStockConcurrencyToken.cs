using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPOS.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddProductStockConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StockVersion",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StockVersion",
                table: "Products");
        }
    }
}
