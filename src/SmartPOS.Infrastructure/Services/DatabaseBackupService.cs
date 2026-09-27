using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Core.Services;

namespace SmartPOS.Infrastructure.Services;

public sealed class DatabaseBackupService(PosDbContext db)
{
    public async Task CreateBackupAsync(string destinationPath, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Database.Backup", ct);
        EnsureSqlite();
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var sourcePath = db.Database.GetDbConnection().DataSource;
        if (string.IsNullOrWhiteSpace(sourcePath) || sourcePath == ":memory:") throw new InvalidOperationException("Database backup requires a file-backed SQLite database.");
        var fullPath = Path.GetFullPath(destinationPath);
        if (string.Equals(Path.GetFullPath(sourcePath), fullPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Backup destination must differ from the active database file.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await db.Database.OpenConnectionAsync(ct);
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString());
        await source.OpenAsync(ct); await target.OpenAsync(ct); source.BackupDatabase(target);
    }

    public async Task RestoreAsync(string backupPath, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Database.Restore", ct);
        EnsureSqlite();
        var fullBackup = Path.GetFullPath(backupPath);
        if (!File.Exists(fullBackup)) throw new FileNotFoundException("Backup file was not found.", fullBackup);
        var sourcePath = db.Database.GetDbConnection().DataSource;
        if (string.IsNullOrWhiteSpace(sourcePath) || sourcePath == ":memory:") throw new InvalidOperationException("Restore requires a file-backed SQLite database.");
        var activePath = Path.GetFullPath(sourcePath);
        if (string.Equals(Path.GetFullPath(fullBackup), activePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Select a backup file that differs from the active database.");
        var safetyCopy = activePath + ".before-restore-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff") + ".bak";
        await CreateBackupAsync(safetyCopy, userId, ct);
        await db.Database.CloseConnectionAsync();
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fullBackup, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath }.ToString());
        await source.OpenAsync(ct); await target.OpenAsync(ct); source.BackupDatabase(target);
    }

    private void EnsureSqlite()
    {
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            throw new NotSupportedException("This backup service supports SQLite databases only.");
    }
}
