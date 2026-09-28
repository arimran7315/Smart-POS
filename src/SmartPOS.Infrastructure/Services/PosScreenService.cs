using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Data;

namespace SmartPOS.Infrastructure.Services;

public sealed class PosScreenService(PosDbContext db)
{
    public async Task<PosScreenSetup> GetSetupAsync(int userId, string defaultStoreName, CancellationToken ct = default)
    {
        var role = await ServiceAuthorization.RequireAndGetRoleAsync(db, userId, "Sales.Create", ct);
        var storeName = await db.Settings.AsNoTracking().Where(x => x.Key == "StoreName")
            .Select(x => x.Value).SingleOrDefaultAsync(ct);
        var customers = new List<PosCustomerOption> { new(null, "Walk-in") };
        if (Permissions.Has(role, "Customers.View") || Permissions.Has(role, "Customers.Manage"))
        {
            var options = await db.Customers.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
                .Select(x => new PosCustomerOption(x.Id, x.Name)).ToListAsync(ct);
            customers.AddRange(options);
        }
        return new PosScreenSetup(string.IsNullOrWhiteSpace(storeName) ? defaultStoreName : storeName, customers);
    }
}

public sealed record PosCustomerOption(int? CustomerId, string Name);
public sealed record PosScreenSetup(string StoreName, IReadOnlyList<PosCustomerOption> Customers);
