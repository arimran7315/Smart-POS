# SmartPOS

SmartPOS is an offline-first Windows desktop point-of-sale foundation built with C#, .NET 10 LTS, WPF, and SQLite. It is organized into Core, Infrastructure, App, and Tests projects so the business rules can be developed separately from storage and the desktop interface.

> **Implementation status:** SmartPOS contains working offline workflows and checked-in SQLite migrations. It is not yet production-ready: held sales are not implemented, reports and tests are limited, and printer formats, backup/restore recovery, authorization coverage, and full shop operations still need broader verification.

## Implemented

- Native WPF startup and first-run administrator creation.
- BCrypt password hashing and login against active user accounts.
- SQLite database under `%LOCALAPPDATA%\SmartPOS\smartpos.db`.
- EF Core entities, SQLite persistence, and an initial migration for products, categories, brands, sales, purchases, sale returns, users, customers, suppliers, payments, expenses, inventory movements, settings, and audit entries.
- POS barcode/SKU/name lookup, duplicate scan quantity increments, stock checks, cart, totals, payment selection, and keyboard shortcuts.
- Transactional sale completion, invoice numbering, stock deduction, payment record, and audit entry.
- WPF dashboard populated from persisted sales, purchases, inventory, customers, suppliers, and expenses data; profit is hidden unless the current role has permission.
- Product catalogue screen with search, add/edit, soft deactivate, SKU/barcode uniqueness checks, and stock adjustments recorded to the movement ledger.
- Purchase receiving and stock adjustments with stock movement history.
- Sales returns that validate quantity against units sold and return stock.
- Supplier purchase returns with partial/full quantity validation, value history, stock deduction, audit entries, and a dedicated tab in the purchasing window.
- Role-checked Infrastructure services for products, customers, suppliers, expenses, credit balance payments, user creation/deactivation, settings, reporting, and SQLite backup/restore.
- Daily sales, top product, and gross profit report queries with date range checks and service-side profit authorization.
- Provider selection through `appsettings.json`; SQLite is the default and SQL Server can be selected through a protected connection string override.
- Sale arithmetic and role permission rule unit tests.
- SQL Server EF Core provider reference for a future provider configuration.

Receipt and Code 128 barcode label printing use the Windows print dialog. A configured Windows printer/driver and matching media are required. Raw ESC/POS output, 58 mm/80 mm formatting, and physical printer layouts have not been verified.

The dashboard links to an operations window for customer and supplier records, expense entry, authorized credit-balance payments, configurable store/invoice settings, database backup/restore, user/role management, and activity history. Purchase receiving, sales/returns, reports, products, POS, dashboard, receipts, and barcode labels each have screens. Category/brand support currently allows adding names; complete editing and deactivation flows are still missing. Restore creates a safety copy and requires an application restart.

## Not implemented yet

Held/resumed sales, mixed tender payments, report charts and exports, category/brand editing and deactivation, printer selection and ESC/POS support, and dark theme remain unfinished. SQL Server has a provider package and configuration option, but only SQLite has a checked-in and tested migration. Automated tests cover selected calculations, password hashing, backup restore, and SQLite workflows; they do not cover every business service. Several windows still use code-behind event handlers, so MVVM conversion is incomplete.

## Technology

- C# and .NET 10 LTS
- Windows Presentation Foundation (WPF)
- Entity Framework Core 10
- SQLite as the local provider
- BCrypt.Net-Next for password hashing
- ZXing.Net for Code 128 barcode label rendering
- xUnit for business logic tests

## Requirements

- Windows 10 or Windows 11 x64
- .NET 10 SDK to build, or the .NET 10 Desktop Runtime to run a framework-dependent build
- Visual Studio 2026 18.0 or later with the .NET desktop development workload, or the .NET 10 CLI
- A USB keyboard-wedge barcode scanner is suitable; printer support is not implemented yet

## Build and run

1. Clone the repository and open `SmartPOS.sln` in Visual Studio 2026 (18.0+) or use the .NET 10 SDK.
2. Restore NuGet packages.
3. Build the solution for `net10.0-windows` on Windows.
4. Run `SmartPOS.App`.
5. On the first run, create the administrator username, display name, and password. Passwords must be at least 12 characters. Later runs show the login screen.

CLI commands from the repository root:

```powershell
dotnet restore .\SmartPOS.sln
dotnet build .\SmartPOS.sln -c Release
dotnet test .\SmartPOS.sln -c Release
dotnet run --project .\src\SmartPOS.App\SmartPOS.App.csproj
```

## Configuration and data

The default SQLite database path is `%LOCALAPPDATA%\SmartPOS\smartpos.db`. On startup the application applies checked-in EF Core migrations. Back up the database before replacing application files or changing schema. Store name, address, phone, currency, invoice prefix, and other persisted settings can be edited in the operations window. Migration upgrades from an existing `EnsureCreated` database are not automatic; databases created by an earlier pre-migration build need a reviewed migration path before upgrading.

SQL Server provider registration is available through `Database:Provider` and a protected `ConnectionStrings:SmartPOS` value (or `SMARTPOS_Database__ConnectionString`). The application currently refuses to start with SQL Server selected because a SQL Server migration set and provider-specific deployment verification are not included. SQLite is the supported default. No connection string or credential is committed here.

## Schema migrations

The checked-in initial migration is in `src/SmartPOS.Infrastructure/Data/Migrations/Sqlite`. Startup applies migrations with `MigrateAsync`, and `DatabaseMigrationTests` verifies creation of a fresh SQLite database. The repository includes the `dotnet-ef` local tool manifest and a SQLite design-time factory. To add a migration after changing the model:

```powershell
dotnet tool restore
dotnet ef migrations add <MigrationName> --project .\src\SmartPOS.Infrastructure\SmartPOS.Infrastructure.csproj --startup-project .\src\SmartPOS.Infrastructure\SmartPOS.Infrastructure.csproj --output-dir Data\Migrations\Sqlite
```

Review the migration and generated model snapshot, and test upgrades from a copy of an existing database before shipping updates. SQLite and SQL Server require provider-specific migration sets; only SQLite migrations are currently provided.

## Barcode scanner

Most USB scanners operate as keyboard-wedge devices. Connect the scanner to Windows, focus the barcode/search field, and scan. Pressing Enter after each scan is supported. The POS looks for an exact barcode or SKU first and then a product name match. If a scan yields multiple name matches, choose the intended product. Unknown and out-of-stock products produce a visible message. Configure the scanner's suffix to Enter if it supports suffix programming. Code 128 label generation and printing are available from product management; physical label sizing depends on the printer and media.

## Receipt printer

Receipts are sent through the Windows print dialog and installed printer driver. Printer selection is made in the dialog; there is no persistent default-printer setting. Install and configure a Windows driver first. 58 mm, 80 mm, and ESC/POS formatting have not been verified.

## First-time setup

1. Start SmartPOS and create the first administrator account.
2. Configure store and invoice settings in the operations window.
3. Add categories and brands, then create products with SKU/barcode, prices, tax, minimum stock, and opening stock adjustment.
4. Add customers and suppliers as needed; receive purchases to increase stock.
5. Open the POS, scan a barcode or search by product name/SKU, select a payment method, and complete a sale.
6. Use sales history to review a sale or process a sale return. Print a receipt from the Windows print dialog.

## Roles

Administrator, Manager, Cashier, and Inventory Manager roles are defined in Core. Login verifies active accounts, and services and screens check permissions for their supported operations; sensitive profit queries are permission gated. The authorization matrix and every workflow still need a comprehensive audit before production use.

## Architecture

```text
src/SmartPOS.Core              Domain entities and deterministic business rules
src/SmartPOS.Infrastructure    EF Core context, SQLite persistence, security, workflows
src/SmartPOS.App               WPF startup, views, view models, and printer adapters
tests/SmartPOS.Tests           Unit and SQLite workflow/migration tests
```

Application startup applies the checked-in SQLite migrations.

## Release publish

After installing the .NET 10 SDK on Windows:

```powershell
dotnet publish .\src\SmartPOS.App\SmartPOS.App.csproj -c Release -r win-x64 --self-contained false
```

The output is under `src\SmartPOS.App\bin\Release\net10.0-windows\win-x64\publish`. Use `--self-contained true` to include the .NET runtime, then test the published output on a clean Windows machine.

## Troubleshooting

- **Database does not open:** Confirm the user can write to `%LOCALAPPDATA%\SmartPOS`; inspect the startup error dialog. Back up the database before replacing it.
- **Barcode scanner appears not to work:** Focus the POS search field, check keyboard input in a text editor, and configure an Enter suffix where possible.
- **Product is unknown:** Confirm the scanned text matches the product barcode or SKU exactly and that the product is active.
- **Insufficient stock:** The sale service blocks quantities above current stock. Receive a purchase or record a stock adjustment in product management.
- **Printer does not print:** Confirm the Windows driver and printer work from another application, then choose the printer in the print dialog. Receipt width and ESC/POS handling are not validated.
- **Migration/schema issue:** Startup applies SQLite migrations. Preserve a backup before upgrades; older databases created with `EnsureCreated` need a reviewed migration path.

## Future work

Add held/resumed sales, mixed payments, report charts and exports, full category and brand CRUD, persistent printer configuration, ESC/POS support, SQL Server migrations, stronger backup/restore recovery testing, full authorization coverage, and broader business-service integration tests.

## License

No license has been selected. Add a license before redistributing this repository.
