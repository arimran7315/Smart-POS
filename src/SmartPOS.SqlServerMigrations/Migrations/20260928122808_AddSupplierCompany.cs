using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPOS.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Company",
                table: "Suppliers",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Company",
                table: "Suppliers");
        }
    }
}
