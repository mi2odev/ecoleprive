using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Infrastructure.Security;
using CentreSoutien.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CentreSoutien.Infrastructure.Data;

public sealed class ConnectionStringProvider(AppPaths paths, KeyStore keys, StorageOptions options)
{
    private static int _initialized;

    public static void InitializeSqlite()
    {
        // SQLCipher build of SQLite (encryption support).
        if (Interlocked.Exchange(ref _initialized, 1) == 0) SQLitePCL.Batteries_V2.Init();
    }

    public string ConnectionString => For(paths.Database, options.EncryptDatabase ? keys.Passphrase : null);

    /// <summary>Closes the pooled connections of this database only (other databases in the process are untouched).</summary>
    public void ClearPool()
    {
        using var c = new SqliteConnection(ConnectionString);
        SqliteConnection.ClearPool(c);
    }

    public static string For(string file, string? passphrase, bool pooling = true)
    {
        var b = new SqliteConnectionStringBuilder
        {
            DataSource = file,
            ForeignKeys = true,
            Pooling = pooling,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };
        if (!string.IsNullOrEmpty(passphrase)) b.Password = passphrase;
        return b.ToString();
    }
}

/// <summary>Applies migrations and makes sure the single owner account and the settings row exist.</summary>
public sealed class DatabaseInitializer(IDbContextFactory<AppDbContext> factory, IPasswordHasher hasher, KeyStore keys)
{
    public const string DefaultUsername = "admin";
    /// <summary>Initial password of a fresh installation. The owner must change it at first login.</summary>
    public const string DefaultPassword = "admin";

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        ConnectionStringProvider.InitializeSqlite();
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);

        if (!await db.Settings.AnyAsync(ct))
            db.Settings.Add(new CenterSettings());

        if (!await db.Accounts.AnyAsync(ct))
        {
            db.Accounts.Add(new OwnerAccount
            {
                Username = DefaultUsername,
                PasswordHash = hasher.Hash(DefaultPassword),
                MustChangePassword = true,
                FullName = "Propriétaire",
            });
            keys.SetBackupPassword(DefaultPassword);
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Used by <c>dotnet ef</c> to create migrations (unencrypted scratch database).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        ConnectionStringProvider.InitializeSqlite();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(ConnectionStringProvider.For(Path.Combine(Path.GetTempPath(), "centre-design.db"), null))
            .Options;
        return new AppDbContext(options);
    }
}
