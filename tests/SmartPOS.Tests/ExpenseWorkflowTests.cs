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

    [Fact]
    public async Task UpdatesAndDeletesExpenseWithAuditTrail()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        var service = new ExpenseService(db);

        var expense = await service.RecordAsync("Rent", "Facilities", 1000m, admin.Id, paymentMethod: "Cash", spentOn: new DateOnly(2026, 3, 1));

        var updated = await service.UpdateAsync(expense.Id, "Office rent", "Facilities", 1200m, admin.Id,
            paymentMethod: "Bank transfer", notes: "March", spentOn: new DateOnly(2026, 3, 2));
        Assert.Equal("Office rent", updated.Description);
        Assert.Equal(1200m, updated.Amount);
        Assert.Equal("Bank transfer", updated.PaymentMethod);
        Assert.Equal("March", updated.Notes);
        Assert.Contains(await db.AuditEntries.ToListAsync(), e => e.Action == "Update" && e.EntityName == "Expense");

        await service.DeleteAsync(expense.Id, admin.Id);
        Assert.Empty(await db.Expenses.ToListAsync());
        Assert.Contains(await db.AuditEntries.ToListAsync(), e => e.Action == "Delete" && e.EntityName == "Expense");

        await Assert.ThrowsAsync<PosRuleException>(() => service.UpdateAsync(expense.Id, "Missing", "Facilities", 1m, admin.Id));
        await Assert.ThrowsAsync<PosRuleException>(() => service.DeleteAsync(expense.Id, admin.Id));
    }

    [Fact]
    public async Task UpdateAndDeleteRequirePermission()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options;
        await using var db = new PosDbContext(options);
        await db.Database.MigrateAsync();
        var admin = new AppUser { UserName = "admin", DisplayName = "Admin", PasswordHash = "test", Role = "Administrator" };
        var cashier = new AppUser { UserName = "cashier", DisplayName = "Cashier", PasswordHash = "test", Role = "Cashier" };
        db.Users.AddRange(admin, cashier);
        await db.SaveChangesAsync();
        var service = new ExpenseService(db);
        var expense = await service.RecordAsync("Rent", "Facilities", 1000m, admin.Id);

        await Assert.ThrowsAnyAsync<Exception>(() => service.UpdateAsync(expense.Id, "x", "y", 2m, cashier.Id));
        await Assert.ThrowsAnyAsync<Exception>(() => service.DeleteAsync(expense.Id, cashier.Id));
    }
}
