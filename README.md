# SmartPOS

SmartPOS is an offline-first Windows desktop point-of-sale foundation built with C#, .NET 10 LTS, WPF, and SQLite. It is organized into Core, Infrastructure, App, and Tests projects so the business rules can be developed separately from storage and the desktop interface.

> **Implementation status:** SmartPOS contains working offline workflows and checked-in SQLite migrations. It is not yet production-ready: additional report visualizations, test coverage, printer formats, backup/restore recovery, authorization coverage, and full shop operations still need broader verification.

## Implemented

- Native WPF startup, first-run administrator creation, and a permission-aware dashboard sidebar with explicit sign-out.
- BCrypt password hashing and login against active user accounts; signing out returns to the login screen, while closing the dashboard exits the app.
- Account sign-in lockout after five failed password attempts for 15 minutes, with failures recorded in the audit log.
- SQLite database under `%LOCALAPPDATA%\SmartPOS\smartpos.db`.
- EF Core entities, SQLite persistence, and an initial migration for products, categories, brands, sales, purchases, sale returns, users, customers, suppliers, payments, expenses, inventory movements, settings, and audit entries.
- POS barcode/SKU/name lookup, duplicate scan quantity increments, stock-validated editable cart quantities, administrator-controlled item and bill discounts, live tax calculations, payment selection, and keyboard shortcuts.
- POS product lookup, barcode/SKU/name scan handling, and ambiguous-match selection by SKU/barcode/price/stock are ViewModel commands; exact single matches add immediately, while unknown and out-of-stock scans report through bound status text. Remove, hold, resume, clear, cancel, new-sale, and complete-sale actions also use ViewModel commands. Enter/F-key focus handling and Windows printing remain view-specific.
- Per-cashier held-sale storage, resume, and confirmed cancel controls, including customer, notes, item discount rates, and bill discount; completing a resumed sale removes the held record in the same transaction, and cancellations are audited.
- Cash/card mixed tender checkout with separate payment records and exact invoice-total validation.
- Transactional sale completion, invoice numbering, stock deduction, payment record, and audit entry.
- Sales history supports invoice, local date-range, and status filters, role-based sale visibility, and stable 100-row pages with total counts; users can reprint receipts for non-voided sales using current printer settings, with the original cashier/customer details and an authorized audit entry. Administrator-only sale voiding reverses stock and recorded tenders transactionally, excludes voided sales from financial reports, and preserves the sale and reason in the audit history. A sale with processed returns cannot be voided.
- WPF dashboard with persisted inventory/customer/supplier metrics, permission-scoped sales, purchase, and expense totals, role-scoped top-selling products and recent activity, plus seven-day sales/purchase charts and permission-gated profit.
- Product catalogue screen with paged name/SKU/barcode search, category and low-stock filters, name/SKU/price/stock sorting, add/edit, soft deactivate, SKU/barcode uniqueness checks, product-image selection and local storage, minimum/maximum stock thresholds, and stock adjustments recorded to the movement ledger. Concurrent stock changes use an optimistic version check so stale till writes fail safely instead of replacing newer stock. A configured maximum is enforced when saving opening stock, receiving purchases, and increasing stock; a maximum of zero leaves stock uncapped.
- Purchase receiving supports per-line percentage discounts and tax, stores invoice/item discount and tax totals, updates stock movements, and sets product purchase cost to the discounted cost before input tax. Supplier returns refund the original net line value proportionally, including the exact remaining rounded balance on the final return.
- Purchase invoices track paid, remaining, and payment status. Administrators can record an initial payment while receiving stock or partial/final payments later; each supplier payment is linked to the invoice and supplier ledger in the same transaction as the invoice balance update.
- Sales returns that validate quantity against units sold and return stock.
- Supplier purchase returns with partial/full quantity validation, value history, stock deduction, audit entries, and a dedicated tab in the purchasing window.
- Role-checked Infrastructure services for products, customers, suppliers (including company, contact details, and opening balances), expenses (including date, method, and notes), invoice and customer account receipts, administrator-only supplier and purchase invoice payments, user creation/deactivation, settings, reporting, and SQLite backup/restore.
- Date-filtered reports for daily sales, cashier/customer sales, top products, daily profit, purchases by day/supplier, expenses, payments, returns, tax, stock movements, and customer/supplier ledgers with brought-forward balances and supplier payments. Inventory valuation estimates on-hand value using each active product's current purchase cost; the low-stock report lists active products at or below their configured minimum and shows the reorder quantity. Data-backed bar charts cover daily sales, sales by cashier, top products, daily profit, daily purchases, purchases by supplier, expenses by category, daily tax, and the top low-stock reorder quantities. Profit queries and charts are permission gated; report tables can be previewed and printed, or exported to CSV with spreadsheet-formula-like text neutralized.
- Provider selection through `appsettings.json`; SQLite is the default and SQL Server can be selected through a protected connection string override.
- Sale arithmetic and role permission rule unit tests, plus parallel SQLite workflow tests for unique sale/purchase invoices, customer credit-limit enforcement, concurrent customer account receipts, user administrator safety, and serialized failed-login lockouts.
- SQL Server EF Core provider reference for a future provider configuration.

Receipt and Code 128 barcode label printing use the Windows print dialog by default. Set `ReceiptPrintMode` to `EscPos` and `DefaultPrinter` to an installed printer queue to send raw ESC/POS receipts directly through the Windows spooler; any other `ReceiptPrintMode` value keeps the print-dialog flow. ESC/POS output supports common initialization, alignment, bold, line feed, and cutter commands and emits portable ASCII (accented text is transliterated; unsupported characters become `?`). Receipt layout accepts 58 mm or 80 mm roll settings and includes the configured store tax number and selected customer. A compatible ESC/POS printer/driver and matching media are required; physical printer layouts and cutter behavior have not been verified.

The dashboard links to an operations window for customer and supplier records, expense entry, invoice and customer account receipts, administrator-only supplier payments, configurable store/invoice settings, database backup/restore, user/role management, and activity history. Purchase receiving, sales/returns, reports, products, POS, dashboard, receipts, and barcode labels each have screens. Customer and supplier forms include contact information and opening balances; customer opening debt is counted against the configured credit limit. Once account activity begins, opening balances are locked so edits cannot rewrite ledger history. Categories and brands can be created, renamed, and soft-deactivated from product management; deactivation preserves links from existing products and writes an audit entry. Backup creation validates a temporary copy before replacing the destination. Restore validates SQLite integrity, foreign-key references, and known migration history before making a safety copy and replacing the live database; restart SmartPOS after restore.

## Not implemented yet

SQL Server has checked-in migrations and provider discovery tests, but live-server deployment and workflow verification are still outstanding. SQL Server backup and restore are not implemented in this build. Automated tests cover selected calculations, password hashing, backup restore, and SQLite workflows, including concurrent invoice numbering, customer credit limits, administrator safety, and login lockout; they do not cover every business service. Several windows still use code-behind event handlers, so MVVM conversion is incomplete. Physical printer output, roll sizing, and cutter behavior remain dependent on verification with target hardware.

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
- A USB keyboard-wedge barcode scanner is suitable; basic Windows print-dialog support is implemented, while physical receipt sizing remains unverified

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

GitHub Actions runs restore, Release build, and the complete test suite on a Windows runner for each push and pull request (`.github/workflows/ci.yml`). It also publishes the self-contained x64 app and builds/verifies the per-user setup executable on the runner; the generated files remain in that workflow run and are not uploaded by CI.

## Configuration and data

Set `SMARTPOS_DATA_DIRECTORY` to override the SmartPOS data root for portable deployments or isolated verification; the database, logs, and product images use that directory. By default, data remains under `%LOCALAPPDATA%\SmartPOS`.

The default SQLite database path is `%LOCALAPPDATA%\SmartPOS\smartpos.db`. Product images selected in the catalogue are copied to `%LOCALAPPDATA%\SmartPOS\ProductImages` and referenced by path from each product record. On startup the application applies checked-in EF Core migrations. If an existing SQLite database has no migration history, startup baselines it only when all application tables and columns exactly match the checked-in initial migration; subsequent migrations are then applied normally. An unrecognized or incompatible schema is left unchanged and startup stops with a recovery message. Back up the database before replacing application files or changing schema. Store name, address, phone, currency, invoice prefix, theme, and other persisted settings can be edited in the operations window. Set `Theme` to `Light` or `Dark` to change the palette live; the selected palette is restored at startup. The published `appsettings.json` is read from beside the executable, independent of the launcher's current working directory. Keep deployment-specific provider and database filename changes there; keep SQL Server credentials in environment configuration rather than a committed file.

SQL Server can be selected with `Database:Provider` and a protected `ConnectionStrings:SmartPOS` value (or `SMARTPOS_Database__ConnectionString`). Startup applies the SQL Server migration set from `SmartPOS.SqlServerMigrations`. The SQL Server provider and schema generation are covered without a live database; connect and verify a real SQL Server deployment before using it operationally. Database backup and restore are currently available only for SQLite, and those actions are disabled for SQL Server. No connection string or credential is committed here.

## Schema migrations

The checked-in initial migration is in `src/SmartPOS.Infrastructure/Data/Migrations/Sqlite`. Startup applies migrations with `MigrateAsync`; legacy `EnsureCreated` databases are accepted only when their schema exactly matches the initial migration, and existing records are retained while newer migrations are applied. An incompatible schema is never baselined automatically. `DatabaseMigrationTests` verifies fresh SQLite creation and asserts the SQLite model matches its latest checked-in snapshot; `LegacyDatabaseBootstrapTests` covers the exact-match upgrade and incompatible-schema rejection paths. `SqlServerMigrationTests` asserts SQL Server model/snapshot parity and migration discovery without connecting to a server. The repository includes the `dotnet-ef` local tool manifest and separate provider design-time factories. To add a SQLite migration after changing the model:

```powershell
dotnet tool restore
dotnet ef migrations add <MigrationName> --project .\src\SmartPOS.Infrastructure\SmartPOS.Infrastructure.csproj --startup-project .\src\SmartPOS.Infrastructure\SmartPOS.Infrastructure.csproj --output-dir Data\Migrations\Sqlite
```

For SQL Server migrations, use the `SmartPOS.SqlServerMigrations` project and its SQL Server design-time factory:

```powershell
dotnet ef migrations add <MigrationName> --project .\src\SmartPOS.SqlServerMigrations\SmartPOS.SqlServerMigrations.csproj --startup-project .\src\SmartPOS.SqlServerMigrations\SmartPOS.SqlServerMigrations.csproj --output-dir Migrations
```

Review the migration and generated model snapshot, and test upgrades from a copy of an existing database before shipping updates. Provider-specific migration discovery is covered by `SqlServerMigrationTests`; it does not replace testing against a live SQL Server instance.

## Barcode scanner

Most USB scanners operate as keyboard-wedge devices. Connect the scanner to Windows, focus the barcode/search field, and scan. Pressing Enter after each scan is supported. The POS looks for an exact barcode or SKU first and then a product name match. If a scan yields multiple name matches, choose the intended product. Unknown and out-of-stock products produce a visible message. Configure the scanner's suffix to Enter if it supports suffix programming. Code 128 label generation and printing are available from product management; physical label sizing depends on the printer and media.

## Receipt printer

Install and configure the Windows printer driver first. Set `ReceiptWidthMm` to `58` or `80`; the receipt page follows that roll width and its content height. For the print dialog, set `DefaultPrinter` to the Windows printer queue name to preselect it while allowing the cashier to choose another printer. For direct raw printing, set `ReceiptPrintMode` to `EscPos` and `DefaultPrinter` to the compatible ESC/POS printer queue name; this sends the receipt without opening a dialog. Other values keep the print-dialog workflow. Set `ApplicationName` in Settings to change the title and report identity. `InvoiceNumberFormat` controls sale and purchase invoice IDs with `{PREFIX}`, `{YEAR}`, `{SEQUENCE}`, or padded `{SEQUENCE:D6}` placeholders; sale and purchase prefixes remain separately configurable. `LowStockThreshold` is the default minimum stock for products whose individual minimum is zero. Set `BackupLocation` to remember the backup folder; Settings shows the last successful backup time. Optional `TaxNumber`, `StoreName`, `StoreAddress`, `StorePhone`, and `StoreEmail` values are printed from Settings, and a selected sale customer is shown. Raw mode uses a portable ASCII subset; physical printer output, target driver compatibility, roll sizing, and cutter behavior still need verification with the actual hardware.

## First-time setup

1. Start SmartPOS and create the first administrator account.
2. Configure store and invoice settings in the operations window.
3. Add categories and brands, then create products with SKU/barcode, prices, tax, minimum stock, and opening stock adjustment.
4. Add customers and suppliers as needed; receive purchases to increase stock.
5. Open the POS, scan a barcode or search by product name/SKU, select a payment method, and complete a sale.
6. Use sales history to review a sale or process a sale return. Print a receipt from the Windows print dialog.

## Roles

Administrator, Manager, Cashier, and Inventory Manager roles are defined in Core. Login verifies active accounts, and services and screens check permissions for their supported operations. Cashiers can view only their own sales history, cannot process returns, and cannot create credit sales; managers and administrators can view all sales. Sale voiding is administrator-only; sensitive profit queries are permission gated. The authorization matrix and every workflow still need a comprehensive audit before production use.

## Architecture

```text
src/SmartPOS.Core              Domain entities and deterministic business rules
src/SmartPOS.Infrastructure    EF Core context, SQLite persistence, security, workflows, raw Windows printer transport
src/SmartPOS.App               WPF startup, views, view models, and printer adapters
tests/SmartPOS.Tests           Unit and SQLite workflow/migration tests
```

The desktop host uses dependency-injection scopes for back-office windows. Each window resolves its own scoped EF Core context and services, then releases them when the window closes, keeping change tracking bounded during a long-running POS session.

Application startup applies the checked-in SQLite migrations.

## Release publish

After installing the .NET 10 SDK on Windows:

```powershell
dotnet publish .\src\SmartPOS.App\SmartPOS.App.csproj -c Release -r win-x64 --self-contained false
```

The output is under `src/SmartPOS.App/bin/Release/net10.0-windows/win-x64/publish`. The framework-dependent build requires the .NET 10 Windows Desktop Runtime. To create a self-contained, single-file Windows x64 executable that does not require a separate .NET installation, use:

```powershell
dotnet publish .\src\SmartPOS.App\SmartPOS.App.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o .\artifacts\SmartPOS-win-x64
```

The executable is `artifacts\SmartPOS-win-x64\SmartPOS.App.exe`. Ship it with the adjacent `appsettings.json` when deployment-specific configuration is needed; do not ship the local `%LOCALAPPDATA%\SmartPOS` database or logs as application files. The self-contained executable was smoke-launched and verified to initialize a fresh SQLite database. Test on a clean Windows machine before operational deployment.

To create a per-user setup executable after publishing, run `powershell -ExecutionPolicy Bypass -File .\packaging\SmartPOS\Build-Installer.ps1`. This uses Windows IExpress to create `artifacts\SmartPOS-Setup.exe`. The installer places program files under `%LOCALAPPDATA%\Programs\SmartPOS`, creates a Start Menu shortcut and a Windows uninstall entry, and launches SmartPOS. Uninstall removes the program and shortcut but deliberately keeps the database, logs, and product images under `%LOCALAPPDATA%\SmartPOS`.

## Troubleshooting

- **Database does not open:** Confirm the user can write to `%LOCALAPPDATA%\SmartPOS`; inspect the startup error dialog. Back up the database before replacing it.
- **Barcode scanner appears not to work:** Focus the POS search field, check keyboard input in a text editor, and configure an Enter suffix where possible.
- **Product is unknown:** Confirm the scanned text matches the product barcode or SKU exactly and that the product is active.
- **Insufficient stock:** The sale service blocks quantities above current stock. Receive a purchase or record a stock adjustment in product management.
- **Printer does not print:** Confirm the Windows driver and printer work from another application. In dialog mode, choose the printer in the print dialog. In `EscPos` mode, check that `DefaultPrinter` exactly matches an installed queue and that the printer accepts raw ESC/POS commands.
- **Migration/schema issue:** Startup applies migrations for the configured provider. Preserve a backup before SQLite upgrades. A legacy `EnsureCreated` database must match the checked-in initial schema exactly; if it does not, startup leaves it untouched and requires a version-specific recovery plan.

## Future work

Add further report charts, SQL Server live-deployment and backup/restore verification, full authorization coverage, and broader business-service integration tests.

## License

No license has been selected. Add a license before redistributing this repository.
