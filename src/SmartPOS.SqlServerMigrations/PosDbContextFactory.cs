using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SmartPOS.Infrastructure.Data;

namespace SmartPOS.SqlServerMigrations;

public sealed class PosDbContextFactory : IDesignTimeDbContextFactory<PosDbContext>
{
    public PosDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PosDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SmartPOSDesignTime;Trusted_Connection=True;TrustServerCertificate=True",
                sql => sql.MigrationsAssembly(typeof(PosDbContextFactory).Assembly.GetName().Name))
            .Options;
        return new PosDbContext(options);
    }
}
