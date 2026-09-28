using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Core.Services;
using SmartPOS.Core.Entities;

namespace SmartPOS.Infrastructure.Services;

public sealed class DatabaseBackupService(PosDbContext db, ILogger<DatabaseBackupService>? logger = null)
{
    public bool SupportsCurrentProvider => db.Database.IsSqlite();

    public Task CreateBackupAsync(string destinationPath, int userId, CancellationToken ct = default) =>
        CreateBackupCoreAsync(destinationPath, userId, ct, recordMetadata: true);

    private async Task CreateBackupCoreAsync(string destinationPath, int userId, CancellationToken ct, bool recordMetadata)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Database.Backup", ct);
        EnsureSqlite();
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var sourcePath = db.Database.GetDbConnection().DataSource;
        if (string.IsNullOrWhiteSpace(sourcePath) || sourcePath == ":memory:") throw new PosRuleException("Database backup requires a file-backed SQLite database.");
        var fullPath = Path.GetFullPath(destinationPath);
        if (string.Equals(Path.GetFullPath(sourcePath), fullPath, StringComparison.OrdinalIgnoreCase)) throw new PosRuleException("Backup destination must differ from the active database file.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await db.Database.OpenConnectionAsync(ct);
            await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly }.ToString()))
            await using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporaryPath }.ToString()))
            {
                await source.OpenAsync(ct); await target.OpenAsync(ct); source.BackupDatabase(target);
            }
            await ValidateBackupAsync(temporaryPath, ct);
            SqliteConnection.ClearAllPools();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        if (recordMetadata) await RecordBackupMetadataAsync(fullPath, userId, ct);
    }

    public async Task RestoreAsync(string backupPath, int userId, CancellationToken ct = default)
    {
        await ServiceAuthorization.RequireAsync(db, userId, "Database.Restore", ct);
        EnsureSqlite();
        var fullBackup = Path.GetFullPath(backupPath);
        if (!File.Exists(fullBackup)) throw new FileNotFoundException("Backup file was not found.", fullBackup);
        var sourcePath = db.Database.GetDbConnection().DataSource;
        if (string.IsNullOrWhiteSpace(sourcePath) || sourcePath == ":memory:") throw new PosRuleException("Restore requires a file-backed SQLite database.");
        var activePath = Path.GetFullPath(sourcePath);
        if (string.Equals(Path.GetFullPath(fullBackup), activePath, StringComparison.OrdinalIgnoreCase)) throw new PosRuleException("Select a backup file that differs from the active database.");
        await ValidateBackupAsync(fullBackup, ct);
        var stagingPath = Path.Combine(Path.GetDirectoryName(activePath)!, $".{Path.GetFileName(activePath)}.{Guid.NewGuid():N}.restore.tmp");
        var safetyCopy = activePath + ".before-restore-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff") + ".bak";
        try
        {
            await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fullBackup, Mode = SqliteOpenMode.ReadOnly }.ToString()))
            await using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = stagingPath }.ToString()))
            {
                await source.OpenAsync(ct);
                await target.OpenAsync(ct);
                source.BackupDatabase(target);
            }
            await ValidateBackupAsync(stagingPath, ct);
            await CreateBackupCoreAsync(safetyCopy, userId, ct, recordMetadata: false);
            await db.Database.CloseConnectionAsync();
            SqliteConnection.ClearAllPools();

            // The staged file shares the live database directory, so replacement is a single
            // filesystem operation; a failed replacement leaves the current database in place.
            File.Replace(stagingPath, activePath, destinationBackupFileName: null, ignoreMetadataErrors: true);

            db.ChangeTracker.Clear();
            await RecordBackupMetadataAsync(safetyCopy, userId, ct, throwOnFailure: false,
                updateBackupLocation: false, recordRestore: true, allowMissingActor: true);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(stagingPath)) File.Delete(stagingPath);
        }
    }

    private async Task RecordBackupMetadataAsync(string backupPath, int userId, CancellationToken ct,
        bool throwOnFailure = true, bool updateBackupLocation = true, bool recordRestore = false, bool allowMissingActor = false)
    {
        try
        {
            var backupLocation = Path.GetDirectoryName(backupPath)!;
            var backupTime = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            if (updateBackupLocation) await SetSettingAsync("BackupLocation", backupLocation, ct);
            await SetSettingAsync("LastBackupUtc", backupTime, ct);
            var actorExists = !allowMissingActor || await db.Users.AsNoTracking().AnyAsync(x => x.Id == userId, ct);
            db.AuditEntries.Add(new AuditEntry { UserId = actorExists ? userId : null, Action = "Backup", EntityName = "Database",
                Description = recordRestore ? "Created an automatic safety backup before restore." : "Created a database backup." });
            if (recordRestore)
            {
                db.AuditEntries.Add(new AuditEntry { UserId = actorExists ? userId : null, Action = "Restore", EntityName = "Database",
                    Description = "Restored a database backup; automatic safety copy was created." });
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Database backup was created at {BackupPath}, but its metadata could not be recorded", backupPath);
            db.ChangeTracker.Clear();
            if (throwOnFailure) throw new PosRuleException("The backup file was created, but its date and activity entry could not be saved.");
        }
    }

    private async Task SetSettingAsync(string key, string value, CancellationToken ct)
    {
        var setting = await db.Settings.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (setting is null) db.Settings.Add(new AppSetting { Key = key, Value = value });
        else { setting.Value = value; setting.UpdatedAtUtc = DateTime.UtcNow; }
    }

    private async Task ValidateBackupAsync(string path, CancellationToken ct)
    {
        await using var candidate = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        try { await candidate.OpenAsync(ct); }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 26 or 14)
        { throw new PosRuleException("The selected file is not a readable SQLite database backup."); }
        await using (var integrity = candidate.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            try
            {
                if (!string.Equals(Convert.ToString(await integrity.ExecuteScalarAsync(ct)), "ok", StringComparison.OrdinalIgnoreCase))
                    throw new PosRuleException("The selected database backup failed its SQLite integrity check.");
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 26 or 11)
            { throw new PosRuleException("The selected database backup failed its SQLite integrity check."); }
        }
        await using (var foreignKeys = candidate.CreateCommand())
        {
            foreignKeys.CommandText = "PRAGMA foreign_key_check;";
            await using var reader = await foreignKeys.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct)) throw new PosRuleException("The selected database backup contains broken references between records.");
        }
        await using (var migrations = candidate.CreateCommand())
        {
            migrations.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;";
            List<string> applied;
            try
            {
                await using var reader = await migrations.ExecuteReaderAsync(ct);
                applied = [];
                while (await reader.ReadAsync(ct)) applied.Add(reader.GetString(0));
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
            { throw new PosRuleException("The selected file is not a migrated SmartPOS database."); }

            var migrationIds = db.Database.GetMigrations().ToArray();
            var known = migrationIds.ToHashSet(StringComparer.Ordinal);
            if (migrationIds.Length == 0 || applied.Count == 0 || applied.Any(id => !known.Contains(id)) || !applied.Contains(migrationIds[0], StringComparer.Ordinal))
                throw new PosRuleException("The selected database backup has an unsupported SmartPOS migration history.");
        }
    }

    private void EnsureSqlite()
    {
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            throw new NotSupportedException("This backup service supports SQLite databases only.");
    }
}
