using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;

namespace SmartPOS.Infrastructure.Services;

public sealed class BackOfficeQueryService(PosDbContext db)
{
    public async Task<IReadOnlyList<Expense>> GetRecentExpensesAsync(int userId, int take = 25, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Expenses.Manage", ct);
        take = Math.Clamp(take, 1, 100);
        return await db.Expenses.AsNoTracking().OrderByDescending(x => x.SpentAtUtc).ThenByDescending(x => x.Id)
            .Take(take).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OpenCreditSale>> GetOpenCreditSalesAsync(int userId, int take = 500, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Payments.Receive", ct);
        take = Math.Clamp(take, 1, 1000);
        return await (from sale in db.Sales.AsNoTracking()
                      join customer in db.Customers.AsNoTracking() on sale.CustomerId equals (int?)customer.Id into customerRows
                      from customer in customerRows.DefaultIfEmpty()
                      where sale.RemainingAmount > 0 && sale.Status != "Voided"
                      orderby sale.SoldAtUtc descending, sale.Id descending
                      select new OpenCreditSale(sale.Id, sale.InvoiceNumber, customer == null ? "Unknown customer" : customer.Name,
                          sale.RemainingAmount))
            .Take(take).ToListAsync(ct);
    }
}

public sealed record OpenCreditSale(int Id, string InvoiceNumber, string CustomerName, decimal Balance);
