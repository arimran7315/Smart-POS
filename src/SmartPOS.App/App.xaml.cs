using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using SmartPOS.App.Views;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Security;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App;

public partial class App : Application
{
    private readonly IHost _host;
    private IServiceScope? _scope;
    private int? _activeUserId;
    public App()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartPOS");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "logs"));
        _host = Host.CreateDefaultBuilder().UseSerilog((_, logger) => logger.MinimumLevel.Information()
            .WriteTo.File(Path.Combine(folder, "logs", "smartpos-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14))
            .ConfigureServices((context, services) =>
        {
            var provider = context.Configuration["Database:Provider"] ?? "Sqlite";
            services.AddDbContext<PosDbContext>(options =>
            {
                if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                {
                    var connection = context.Configuration.GetConnectionString("SmartPOS")
                        ?? Environment.GetEnvironmentVariable("SMARTPOS_Database__ConnectionString");
                    if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Set ConnectionStrings:SmartPOS or SMARTPOS_Database__ConnectionString for SQL Server.");
                    options.UseSqlServer(connection);
                }
                else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
                    options.UseSqlite($"Data Source={Path.Combine(folder, context.Configuration["Database:SqliteFileName"] ?? "smartpos.db")}");
                else throw new InvalidOperationException($"Unsupported database provider '{provider}'.");
            });
            services.AddSingleton<PasswordHasher>(); services.AddScoped<AuthenticationService>(); services.AddScoped<SaleService>(); services.AddScoped<InventoryService>(); services.AddScoped<PurchaseService>(); services.AddScoped<ReturnsService>(); services.AddScoped<PurchaseReturnsService>(); services.AddScoped<ProductService>(); services.AddScoped<DashboardService>(); services.AddScoped<DatabaseBackupService>();
            services.AddScoped<CustomerService>(); services.AddScoped<SupplierService>(); services.AddScoped<ExpenseService>(); services.AddScoped<PaymentService>(); services.AddScoped<UserManagementService>(); services.AddScoped<SettingsService>();
            services.AddScoped<ReportsService>();
            services.AddScoped<CatalogTaxonomyService>();
        }).Build();
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            await _host.StartAsync();
            _scope = _host.Services.CreateScope();
            var db = _scope.ServiceProvider.GetRequiredService<PosDbContext>();
            if (db.Database.IsSqlServer())
                throw new NotSupportedException("This build includes SQLite migrations only. Select SQLite or deploy SQL Server after provider-specific migrations are added.");
            await db.Database.MigrateAsync();
            Log.Information("SmartPOS database is ready. Provider: {Provider}", db.Database.ProviderName);
            var applicationName = _scope.ServiceProvider.GetRequiredService<IConfiguration>()["Application:Name"] ?? "SmartPOS";
            Resources["AppName"] = applicationName;
            var login = new LoginWindow(_scope.ServiceProvider.GetRequiredService<AuthenticationService>(), db);
            login.Title = $"{applicationName} | Sign in";
            if (login.ShowDialog() != true) { Shutdown(); return; }
            _activeUserId = login.CurrentUser!.Id;
            var window = new DashboardWindow(_scope.ServiceProvider.GetRequiredService<DashboardService>(),
                _scope.ServiceProvider.GetRequiredService<SaleService>(), _scope.ServiceProvider.GetRequiredService<ProductService>(),
                _scope.ServiceProvider.GetRequiredService<InventoryService>(), db, login.CurrentUser!);
            window.Title = $"{applicationName} | Dashboard";
            MainWindow = window; ShutdownMode = ShutdownMode.OnMainWindowClose; window.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "SmartPOS failed during startup");
            MessageBox.Show("SmartPOS could not start. Check the application log under %LOCALAPPDATA%\\SmartPOS\\logs for details.",
                "SmartPOS startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("SmartPOS is shutting down");
        if (_scope is not null && _activeUserId is int userId)
        {
            try { var db = _scope.ServiceProvider.GetRequiredService<PosDbContext>(); db.AuditEntries.Add(new SmartPOS.Core.Entities.AuditEntry { UserId = userId, Action = "SignOut", EntityName = "Authentication", EntityId = userId.ToString(), Description = "User signed out by closing the application." }); await db.SaveChangesAsync(); }
            catch (Exception ex) { Log.Warning(ex, "Could not record application sign-out"); }
        }
        _scope?.Dispose(); await _host.StopAsync(); _host.Dispose(); Log.CloseAndFlush(); base.OnExit(e);
    }
}
