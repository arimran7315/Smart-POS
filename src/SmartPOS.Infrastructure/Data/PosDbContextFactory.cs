using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartPOS.Infrastructure.Data;

public sealed class PosDbContextFactory : IDesignTimeDbContextFactory<PosDbContext>
{
    public PosDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PosDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        return new PosDbContext(options);
    }
}
