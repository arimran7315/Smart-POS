using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using SmartPOS.App.Services;
using SmartPOS.App.Views;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Security;
using SmartPOS.Infrastructure.Printing;
using SmartPOS.Infrastructure.Services;
using SmartPOS.UseCases.Authentication;
using SmartPOS.UseCases.Catalog;

namespace SmartPOS.App;

public partial class App : Application
{
    private readonly IHost _host;
    private IServiceScope? _scope;
    private int? _activeUserId;
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        var folder = SmartPosPaths.DataDirectory;
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(SmartPosPaths.LogsDirectory);
        _host = Host.CreateDefaultBuilder().UseContentRoot(AppContext.BaseDirectory).UseSerilog((_, logger) => logger.MinimumLevel.Information()
            .WriteTo.File(Path.Combine(SmartPosPaths.LogsDirectory, "smartpos-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14))
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
                    options.UseSqlServer(connection, sql => sql.MigrationsAssembly("SmartPOS.SqlServerMigrations"));
                }
                else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
                    options.UseSqlite($"Data Source={Path.Combine(folder, context.Configuration["Database:SqliteFileName"] ?? "smartpos.db")}");
                else throw new InvalidOperationException($"Unsupported database provider '{provider}'.");
            });
            services.AddSingleton<PasswordHasher>(); services.AddScoped<AuthenticationService>(); services.AddScoped<IAuthenticationService>(provider => provider.GetRequiredService<AuthenticationService>()); services.AddScoped<SaleService>(); services.AddScoped<HeldSaleService>(); services.AddScoped<InventoryService>(); services.AddScoped<IInventoryManagementService>(provider => provider.GetRequiredService<InventoryService>()); services.AddScoped<PurchaseService>(); services.AddScoped<ReturnsService>(); services.AddScoped<PurchaseReturnsService>(); services.AddScoped<ProductService>(); services.AddScoped<IProductCatalogService>(provider => provider.GetRequiredService<ProductService>()); services.AddScoped<DashboardService>(); services.AddScoped<DatabaseBackupService>();
            services.AddScoped<CustomerService>(); services.AddScoped<SupplierService>(); services.AddScoped<ExpenseService>(); services.AddScoped<PaymentService>(); services.AddScoped<CustomerAccountPaymentService>(); services.AddScoped<SupplierPaymentService>(); services.AddScoped<UserManagementService>(); services.AddScoped<SettingsService>();
            services.AddScoped<ReportsService>(); services.AddScoped<PosScreenService>(); services.AddScoped<BackOfficeQueryService>(); services.AddScoped<AuditService>();
            services.AddScoped<QuotationService>(); services.AddScoped<ShiftService>();
            services.AddScoped<CatalogTaxonomyService>(); services.AddScoped<ICatalogTaxonomyService>(provider => provider.GetRequiredService<CatalogTaxonomyService>());
            services.AddSingleton<IRawPrinter, WindowsRawPrinter>();
            services.AddTransient<ReceiptPrinter>();
        }).Build();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var applicationName = SmartPosIdentity.Name;
        Log.Fatal(e.Exception, "Unhandled exception on the WPF dispatcher; closing {ApplicationName}", applicationName);
        e.Handled = true;
        MessageBox.Show($"An unexpected problem occurred. {applicationName} will close. Check the application log under {SmartPosPaths.LogsDirectory} for details.",
            $"{applicationName} error", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            Log.Fatal(exception, "Unhandled exception outside the WPF dispatcher; terminating: {IsTerminating}", e.IsTerminating);
        else
            Log.Fatal("Unhandled non-exception object outside the WPF dispatcher; terminating: {IsTerminating}", e.IsTerminating);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "An unobserved background task failed");
        e.SetObserved();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Show a branded splash immediately so launching feels responsive while the host
        // starts and the database migrates, with a busy cursor like other desktop apps.
        var splash = new SplashScreen("Assets/Splash.png");
        splash.Show(autoClose: false, topMost: true);
        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.AppStarting;
        var splashClosed = false;
        void CloseSplash()
        {
            if (splashClosed) return;
            splashClosed = true;
            System.Windows.Input.Mouse.OverrideCursor = null;
            splash.Close(TimeSpan.FromMilliseconds(300));
        }
        try
        {
            var defaultApplicationName = _host.Services.GetRequiredService<IConfiguration>()["Application:Name"] ?? "SmartPOS";
            Resources["AppName"] = defaultApplicationName;
            await _host.StartAsync();
            _scope = _host.Services.CreateScope();
            var db = _scope.ServiceProvider.GetRequiredService<PosDbContext>();
            if (db.Database.IsSqlite()) await SqliteLegacyDatabaseBootstrapper.PrepareForMigrationsAsync(db);
            await db.Database.MigrateAsync();
            Log.Information("{ApplicationName} database is ready. Provider: {Provider}", SmartPosIdentity.Name, db.Database.ProviderName);
            var theme = await db.Settings.AsNoTracking().Where(x => x.Key == "Theme").Select(x => x.Value).SingleOrDefaultAsync();
            AppTheme.Apply(this, theme);
            var configuredApplicationName = await db.Settings.AsNoTracking().Where(x => x.Key == "ApplicationName").Select(x => x.Value).SingleOrDefaultAsync();
            Resources["AppName"] = string.IsNullOrWhiteSpace(configuredApplicationName) ? defaultApplicationName : configuredApplicationName;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            while (true)
            {
                var applicationName = Convert.ToString(Resources["AppName"]) ?? "SmartPOS";
                var login = new LoginWindow(_scope.ServiceProvider.GetRequiredService<IAuthenticationService>())
                { Title = $"{applicationName} | Sign in" };
                CloseSplash();
                if (login.ShowDialog() != true) { Shutdown(); return; }

                _activeUserId = login.CurrentUser!.Id;
                var window = new DashboardWindow(_scope.ServiceProvider.GetRequiredService<DashboardService>(), login.CurrentUser!,
                    _scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>())
                { Title = $"{applicationName} | Dashboard" };
                MainWindow = window;
                window.ShowDialog();
                if (!window.IsSigningOut) { Shutdown(); return; }

                try
                {
                    db.AuditEntries.Add(new SmartPOS.Core.Entities.AuditEntry { UserId = _activeUserId, Action = "SignOut",
                        EntityName = "Authentication", EntityId = _activeUserId?.ToString(), Description = "User signed out from the dashboard." });
                    await db.SaveChangesAsync();
                }
                catch (Exception ex) { Log.Warning(ex, "Could not record dashboard sign-out"); }
                _activeUserId = null;
                db.ChangeTracker.Clear();
                MainWindow = null;
            }
        }
        catch (Exception ex)
        {
            CloseSplash();
            var applicationName = SmartPosIdentity.Name;
            Log.Fatal(ex, "{ApplicationName} failed during startup", applicationName);
            MessageBox.Show($"{applicationName} could not start. Check the application log under {SmartPosPaths.LogsDirectory} for details.",
                $"{applicationName} startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("{ApplicationName} is shutting down", SmartPosIdentity.Name);
        if (_scope is not null && _activeUserId is int userId)
        {
            try { var db = _scope.ServiceProvider.GetRequiredService<PosDbContext>(); db.AuditEntries.Add(new SmartPOS.Core.Entities.AuditEntry { UserId = userId, Action = "SignOut", EntityName = "Authentication", EntityId = userId.ToString(), Description = "User signed out by closing the application." }); await db.SaveChangesAsync(); }
            catch (Exception ex) { Log.Warning(ex, "Could not record application sign-out"); }
        }
        _scope?.Dispose(); await _host.StopAsync(); _host.Dispose(); Log.CloseAndFlush(); base.OnExit(e);
    }
}
