using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartPOS.Infrastructure.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class StandardizeDecimals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "OpeningBalance",
                table: "Suppliers",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "SupplierPayments",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityDelta",
                table: "StockMovements",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "BalanceAfter",
                table: "StockMovements",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Tax",
                table: "Sales",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Subtotal",
                table: "Sales",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "RemainingAmount",
                table: "Sales",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "PaidAmount",
                table: "Sales",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "GrandTotal",
                table: "Sales",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Discount",
                table: "Sales",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "CashReceivedAmount",
                table: "Sales",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "SaleItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "SaleItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Total",
                table: "SaleItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Tax",
                table: "SaleItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "SaleItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Discount",
                table: "SaleItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Total",
                table: "Returns",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Refund",
                table: "ReturnItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "ReturnItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Total",
                table: "Purchases",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Tax",
                table: "Purchases",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "RemainingAmount",
                table: "Purchases",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "PaidAmount",
                table: "Purchases",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Discount",
                table: "Purchases",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Total",
                table: "PurchaseReturns",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Total",
                table: "PurchaseReturnItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "PurchaseReturnItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "PurchaseItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Total",
                table: "PurchaseItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Tax",
                table: "PurchaseItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "PurchaseItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Discount",
                table: "PurchaseItems",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "WholesalePrice",
                table: "Products",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "TaxRate",
                table: "Products",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "SalePrice",
                table: "Products",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "PurchasePrice",
                table: "Products",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "MinimumStock",
                table: "Products",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "MaximumStock",
                table: "Products",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "DiscountRate",
                table: "Products",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "CurrentStock",
                table: "Products",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "Payments",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "BillDiscount",
                table: "HeldSales",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "Expenses",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "OpeningBalance",
                table: "Customers",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "CreditLimit",
                table: "Customers",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount",
                table: "CustomerAccountPayments",
                type: "TEXT",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.Sql("UPDATE Customers SET CreditLimit = CreditLimit / 10000.0, OpeningBalance = OpeningBalance / 10000.0;");
            migrationBuilder.Sql("UPDATE Suppliers SET OpeningBalance = OpeningBalance / 10000.0;");
            migrationBuilder.Sql("UPDATE Purchases SET Total = Total / 10000.0, PaidAmount = PaidAmount / 10000.0, RemainingAmount = RemainingAmount / 10000.0, Discount = Discount / 10000.0, Tax = Tax / 10000.0;");
            migrationBuilder.Sql("UPDATE PurchaseItems SET Quantity = Quantity / 10000.0, UnitCost = UnitCost / 10000.0, Discount = Discount / 10000.0, Tax = Tax / 10000.0, Total = Total / 10000.0;");
            migrationBuilder.Sql("UPDATE Sales SET Subtotal = Subtotal / 10000.0, Discount = Discount / 10000.0, Tax = Tax / 10000.0, GrandTotal = GrandTotal / 10000.0, PaidAmount = PaidAmount / 10000.0, RemainingAmount = RemainingAmount / 10000.0, CashReceivedAmount = CashReceivedAmount / 10000.0;");
            migrationBuilder.Sql("UPDATE SaleItems SET Quantity = Quantity / 10000.0, UnitPrice = UnitPrice / 10000.0, UnitCost = UnitCost / 10000.0, Discount = Discount / 10000.0, Tax = Tax / 10000.0, Total = Total / 10000.0;");
            migrationBuilder.Sql("UPDATE Expenses SET Amount = Amount / 10000.0;");
            migrationBuilder.Sql("UPDATE Payments SET Amount = Amount / 10000.0;");
            migrationBuilder.Sql("UPDATE SupplierPayments SET Amount = Amount / 10000.0;");
            migrationBuilder.Sql("UPDATE CustomerAccountPayments SET Amount = Amount / 10000.0;");
            migrationBuilder.Sql("UPDATE HeldSales SET BillDiscount = BillDiscount / 10000.0;");
            migrationBuilder.Sql("UPDATE Returns SET Total = Total / 10000.0;");
            migrationBuilder.Sql("UPDATE ReturnItems SET Quantity = Quantity / 10000.0, Refund = Refund / 10000.0;");
            migrationBuilder.Sql("UPDATE PurchaseReturns SET Total = Total / 10000.0;");
            migrationBuilder.Sql("UPDATE PurchaseReturnItems SET Quantity = Quantity / 10000.0, Total = Total / 10000.0;");
            migrationBuilder.Sql("UPDATE StockMovements SET QuantityDelta = QuantityDelta / 10000.0, BalanceAfter = BalanceAfter / 10000.0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "OpeningBalance",
                table: "Suppliers",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Amount",
                table: "SupplierPayments",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "QuantityDelta",
                table: "StockMovements",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "BalanceAfter",
                table: "StockMovements",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Tax",
                table: "Sales",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Subtotal",
                table: "Sales",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "RemainingAmount",
                table: "Sales",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "PaidAmount",
                table: "Sales",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "GrandTotal",
                table: "Sales",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Discount",
                table: "Sales",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "CashReceivedAmount",
                table: "Sales",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "UnitPrice",
                table: "SaleItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "UnitCost",
                table: "SaleItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Total",
                table: "SaleItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Tax",
                table: "SaleItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Quantity",
                table: "SaleItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Discount",
                table: "SaleItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Total",
                table: "Returns",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Refund",
                table: "ReturnItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Quantity",
                table: "ReturnItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Total",
                table: "Purchases",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Tax",
                table: "Purchases",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "RemainingAmount",
                table: "Purchases",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "PaidAmount",
                table: "Purchases",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Discount",
                table: "Purchases",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Total",
                table: "PurchaseReturns",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Total",
                table: "PurchaseReturnItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Quantity",
                table: "PurchaseReturnItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "UnitCost",
                table: "PurchaseItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Total",
                table: "PurchaseItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Tax",
                table: "PurchaseItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Quantity",
                table: "PurchaseItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Discount",
                table: "PurchaseItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "WholesalePrice",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "TaxRate",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "SalePrice",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "PurchasePrice",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "MinimumStock",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "MaximumStock",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "DiscountRate",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "CurrentStock",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Amount",
                table: "Payments",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "BillDiscount",
                table: "HeldSales",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Amount",
                table: "Expenses",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "OpeningBalance",
                table: "Customers",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "CreditLimit",
                table: "Customers",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<long>(
                name: "Amount",
                table: "CustomerAccountPayments",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 18,
                oldScale: 4);
        }
    }
}
