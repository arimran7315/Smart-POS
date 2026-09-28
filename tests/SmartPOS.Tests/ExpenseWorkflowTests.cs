using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;
using Xunit;

namespace SmartPOS.Tests;

public sealed class ExpenseWorkflowTests
{
    [Fact]
    public async Task RecordsExpenseDatePaymentMethodAndNotesAndReportsByMethod()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var date = new DateOnly(2026, 2, 3);
        var expense = await new ExpenseService(db).RecordAsync("Electricity", "Utilities", 125.50m, admin.Id,
            paymentMethod: "Bank transfer", notes: "February invoice", spentOn: date);
        Assert.Equal(date, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(expense.SpentAtUtc, TimeZoneInfo.Local)));
        Assert.Equal("Bank transfer", expense.PaymentMethod);
        Assert.Equal("February invoice", expense.Notes);
        await Assert.ThrowsAsync<PosRuleException>(() => new ExpenseService(db).RecordAsync("Invalid", "Other", 10m, admin.Id, paymentMethod: "Cheque"));

        var report = await new ReportsService(db).GetExpensesAsync(admin.Id, date, date);
        var row = Assert.Single(report);
        Assert.Equal(date, row.Day);
        Assert.Equal("Utilities", row.Category);
        Assert.Equal("Bank transfer", row.PaymentMethod);
        Assert.Equal(125.50m, row.Amount);
    }
}
