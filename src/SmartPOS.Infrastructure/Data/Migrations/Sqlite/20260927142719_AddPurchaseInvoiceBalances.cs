using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPOS.Infrastructure.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddPurchaseInvoiceBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurchaseId",
                table: "SupplierPayments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PaidAmount",
                table: "Purchases",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "Purchases",
                type: "TEXT",
                nullable: false,
                defaultValue: "Unpaid");

            migrationBuilder.AddColumn<long>(
                name: "RemainingAmount",
                table: "Purchases",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_PurchaseId",
                table: "SupplierPayments",
                column: "PurchaseId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierPayments_Purchases_PurchaseId",
                table: "SupplierPayments",
                column: "PurchaseId",
                principalTable: "Purchases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("UPDATE Purchases SET RemainingAmount = Total, PaymentStatus = 'Unpaid';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupplierPayments_Purchases_PurchaseId",
                table: "SupplierPayments");

            migrationBuilder.DropIndex(
                name: "IX_SupplierPayments_PurchaseId",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "PurchaseId",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "PaidAmount",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "RemainingAmount",
                table: "Purchases");
        }
    }
}
