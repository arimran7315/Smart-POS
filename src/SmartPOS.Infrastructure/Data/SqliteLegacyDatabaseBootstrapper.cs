using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SmartPOS.Infrastructure.Data;

/// <summary>
/// Baselines a legacy EnsureCreated SQLite database only when its schema exactly matches
/// the checked-in initial migration. All later schema changes still run through EF migrations.
/// </summary>
public static class SqliteLegacyDatabaseBootstrapper
{
    private const string HistoryTable = "__EFMigrationsHistory";
    private const string MigrationLockTable = "__EFMigrationsLock";

    public static async Task PrepareForMigrationsAsync(PosDbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!db.Database.IsSqlite()) return;

        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State != ConnectionState.Open;
        if (closeWhenDone) await db.Database.OpenConnectionAsync(ct);
        try
        {
            var tableNames = await ReadTableNamesAsync(connection, ct);
            var userTables = tableNames.Where(name => !name.Equals(HistoryTable, StringComparison.OrdinalIgnoreCase)
                && !name.Equals(MigrationLockTable, StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (userTables.Count == 0) return;

            var applied = await db.Database.GetAppliedMigrationsAsync(ct);
            if (applied.Any()) return;
            if (tableNames.Contains(HistoryTable, StringComparer.OrdinalIgnoreCase))
                throw IncompatibleSchema("the migration-history table exists but contains no applied migrations");

            var migrations = db.Database.GetMigrations().ToArray();
            if (migrations.Length == 0) throw IncompatibleSchema("the application has no initial migration");
            var initialMigrationId = migrations[0];
            var assembly = db.GetService<IMigrationsAssembly>();
            if (!assembly.Migrations.TryGetValue(initialMigrationId, out var migrationType))
                throw IncompatibleSchema($"the initial migration '{initialMigrationId}' could not be loaded");
            var initialMigration = assembly.CreateMigration(migrationType, db.Database.ProviderName!);

            var expected = GetModelTables(initialMigration.TargetModel);
            var schemaMismatch = await FindSchemaMismatchAsync(connection, userTables, expected, ct);
            if (schemaMismatch is not null)
                throw IncompatibleSchema($"the existing tables or columns do not exactly match the initial SQLite migration ({schemaMismatch})");
            await EnsureForeignKeysAreValidAsync(connection, ct);

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var historyRepository = db.GetService<IHistoryRepository>();
            await db.Database.ExecuteSqlRawAsync(historyRepository.GetCreateScript(), ct);
            await db.Database.ExecuteSqlRawAsync(historyRepository.GetInsertScript(
                new HistoryRow(initialMigrationId, ProductInfo.GetVersion())), ct);
            await transaction.CommitAsync(ct);
        }
        finally
        {
            if (closeWhenDone) await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<HashSet<string>> ReadTableNamesAsync(System.Data.Common.DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
        await using var reader = await command.ExecuteReaderAsync(ct);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));
        return names;
    }

    private static Dictionary<string, HashSet<string>> GetModelTables(IModel model)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entityType in model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName is null) continue;
            var store = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
            if (!result.TryGetValue(tableName, out var columns)) result[tableName] = columns = new(StringComparer.OrdinalIgnoreCase);
            foreach (var property in entityType.GetProperties())
            {
                var column = property.GetColumnName(store);
                if (column is not null) columns.Add(column);
            }
        }
        return result;
    }

    private static async Task<string?> FindSchemaMismatchAsync(System.Data.Common.DbConnection connection,
        HashSet<string> actualTables, Dictionary<string, HashSet<string>> expectedTables, CancellationToken ct)
    {
        if (!actualTables.SetEquals(expectedTables.Keys))
        {
            var missing = expectedTables.Keys.Except(actualTables, StringComparer.OrdinalIgnoreCase);
            var unexpected = actualTables.Except(expectedTables.Keys, StringComparer.OrdinalIgnoreCase);
            return $"missing tables [{string.Join(", ", missing)}], unexpected tables [{string.Join(", ", unexpected)}]";
        }
        foreach (var (table, expectedColumns) in expectedTables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)});";
            await using var reader = await command.ExecuteReaderAsync(ct);
            var actualColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(ct)) actualColumns.Add(reader.GetString(1));
            if (!actualColumns.SetEquals(expectedColumns))
            {
                var missing = expectedColumns.Except(actualColumns, StringComparer.OrdinalIgnoreCase);
                var unexpected = actualColumns.Except(expectedColumns, StringComparer.OrdinalIgnoreCase);
                return $"table '{table}' has missing columns [{string.Join(", ", missing)}] and unexpected columns [{string.Join(", ", unexpected)}]";
            }
        }
        return null;
    }

    private static async Task EnsureForeignKeysAreValidAsync(System.Data.Common.DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            throw IncompatibleSchema($"a foreign-key violation was found in table '{reader.GetString(0)}'");
    }

    private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static InvalidOperationException IncompatibleSchema(string reason) => new(
        $"SmartPOS found an existing SQLite database without migration history, but {reason}. " +
        "No schema changes were made. Back up the database and migrate it with a version-specific upgrade procedure before starting this build.");
}
