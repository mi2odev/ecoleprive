using CentreSoutien.Application.Abstractions;
using CentreSoutien.Infrastructure;
using CentreSoutien.Infrastructure.Data;
using CentreSoutien.Infrastructure.Security;
using CentreSoutien.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Tests;

/// <summary>A fully wired local backend on a temporary data folder.</summary>
public sealed class TestHost : IAsyncDisposable
{
    public string Folder { get; }
    public ServiceProvider Services { get; }
    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 9, 26, 14, 32, 0, TimeSpan.FromHours(1)));

    private TestHost(string folder, bool encrypt, Action<IServiceCollection>? configure)
    {
        Folder = folder;
        var sc = new ServiceCollection();
        sc.AddLocalInfrastructure(new StorageOptions { DataFolder = folder, EncryptDatabase = encrypt }, new PlainKeyProtector());
        sc.AddSingleton<TimeProvider>(Clock);
        sc.AddSingleton<IPasswordHasher>(new Pbkdf2PasswordHasher(1_000));
        configure?.Invoke(sc);
        Services = sc.BuildServiceProvider();
    }

    public static async Task<TestHost> CreateAsync(string? folder = null, bool encrypt = true, Action<IServiceCollection>? configure = null)
    {
        var host = new TestHost(folder ?? Path.Combine(Path.GetTempPath(), "cs-tests-" + Guid.NewGuid().ToString("N")), encrypt, configure);
        await host.Get<DatabaseInitializer>().InitializeAsync();
        return host;
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Folder, true); } catch (IOException) { }
    }
}

public sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("t", Now.Offset, "t", "t");
    public void Advance(TimeSpan by) => Now += by;
}
