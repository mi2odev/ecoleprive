using System.Text;
using CentreSoutien.Application.Abstractions;
using CentreSoutien.Infrastructure.Data;
using CentreSoutien.Infrastructure.Security;
using CentreSoutien.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace CentreSoutien.Tests;

public class SecurityTests
{
    [Fact]
    public void Hasher_verifies_and_never_stores_plain_text()
    {
        var h = new Pbkdf2PasswordHasher(1_000);
        var hash = h.Hash("Secret123!");
        Assert.DoesNotContain("Secret123!", hash);
        Assert.StartsWith("pbkdf2-sha256$1000$", hash);
        Assert.True(h.Verify("Secret123!", hash));
        Assert.False(h.Verify("secret123!", hash));
        Assert.NotEqual(hash, h.Hash("Secret123!")); // random salt
        Assert.True(new Pbkdf2PasswordHasher(600_000).NeedsRehash(hash));
    }

    [Fact]
    public void Password_policy_rejects_weak_passwords()
    {
        Assert.NotNull(PasswordPolicy.Validate("short1"));
        Assert.NotNull(PasswordPolicy.Validate("onlyletters"));
        Assert.NotNull(PasswordPolicy.Validate("admin123", "admin123"));
        Assert.Null(PasswordPolicy.Validate("Centre2026!", "admin"));
    }

    [Fact]
    public async Task Fresh_install_has_single_admin_account_that_must_change_password()
    {
        await using var host = await TestHost.CreateAsync();
        var auth = host.Get<IAuthService>();
        var result = await auth.LoginAsync("admin", DatabaseInitializer.DefaultPassword);
        Assert.True(result.Succeeded);
        Assert.True(result.Account!.MustChangePassword);
        Assert.False((await auth.LoginAsync("admin", "wrong")).Succeeded);
        Assert.False((await auth.LoginAsync("other", DatabaseInitializer.DefaultPassword)).Succeeded);

        await Assert.ThrowsAsync<BusinessException>(() => auth.ChangePasswordAsync("admin", "admin"));
        await auth.ChangePasswordAsync("admin", "Centre2026!");
        var after = await auth.LoginAsync("ADMIN", "Centre2026!");
        Assert.True(after.Succeeded);
        Assert.False(after.Account!.MustChangePassword);
        Assert.False((await auth.LoginAsync("admin", "admin")).Succeeded);
    }

    [Fact]
    public async Task Repeated_failures_lock_out_temporarily()
    {
        await using var host = await TestHost.CreateAsync();
        var auth = host.Get<IAuthService>();
        for (var i = 0; i < 4; i++) Assert.Equal(LoginOutcome.InvalidCredentials, (await auth.LoginAsync("admin", "nope")).Outcome);
        Assert.Equal(LoginOutcome.LockedOut, (await auth.LoginAsync("admin", "nope")).Outcome);
        // Even the right password is refused during the lockout.
        Assert.Equal(LoginOutcome.LockedOut, (await auth.LoginAsync("admin", "admin")).Outcome);
        host.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True((await auth.LoginAsync("admin", "admin")).Succeeded);
    }

    [Fact]
    public async Task Username_can_be_changed()
    {
        await using var host = await TestHost.CreateAsync();
        var auth = host.Get<IAuthService>();
        await auth.UpdateProfileAsync("directeur", "Mourad Benyahia", "m@x.dz", null, null);
        Assert.True((await auth.LoginAsync("directeur", "admin")).Succeeded);
        Assert.False((await auth.LoginAsync("admin", "admin")).Succeeded);
        await Assert.ThrowsAsync<BusinessException>(() => auth.UpdateProfileAsync("a b", "x", null, null, null));
    }

    [Fact]
    public void Session_locks_after_inactivity_and_expires_while_locked()
    {
        var clock = new FakeClock(DateTimeOffset.Now);
        var s = new AppSession(clock) { AutoLockMinutes = 10, SessionTimeoutMinutes = 60 };
        s.SignIn(new() { Username = "admin" });
        Assert.Equal(SessionState.Active, s.State);
        clock.Advance(TimeSpan.FromMinutes(9));
        s.Tick();
        Assert.Equal(SessionState.Active, s.State);
        s.Touch();
        clock.Advance(TimeSpan.FromMinutes(9));
        s.Tick();
        Assert.Equal(SessionState.Active, s.State);
        clock.Advance(TimeSpan.FromMinutes(1));
        s.Tick();
        Assert.Equal(SessionState.Locked, s.State);
        s.Touch(); // activity while locked does not count
        clock.Advance(TimeSpan.FromMinutes(60));
        s.Tick();
        Assert.Equal(SessionState.SignedOut, s.State);
        Assert.Null(s.Account);
    }

    [Fact]
    public void Session_requires_password_change_first()
    {
        var s = new AppSession();
        var acc = new Domain.Entities.OwnerAccount { MustChangePassword = true };
        s.SignIn(acc);
        Assert.Equal(SessionState.PasswordChangeRequired, s.State);
        acc.MustChangePassword = false;
        s.PasswordChanged(acc);
        Assert.Equal(SessionState.Active, s.State);
    }

    [Fact]
    public async Task Database_file_is_encrypted()
    {
        await using var host = await TestHost.CreateAsync();
        var paths = host.Get<AppPaths>();
        host.Get<ConnectionStringProvider>().ClearPool();
        var head = new byte[16];
        await using (var fs = new FileStream(paths.Database, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            await fs.ReadExactlyAsync(head);
        Assert.NotEqual("SQLite format 3\0", Encoding.ASCII.GetString(head));

        // Opening without the key fails.
        using var conn = new SqliteConnection(ConnectionStringProvider.For(paths.Database, null, pooling: false));
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master";
        Assert.ThrowsAny<SqliteException>(() => cmd.ExecuteScalar());
    }

    [Fact]
    public void Password_wrap_round_trips_only_with_right_password()
    {
        var secret = new byte[] { 1, 2, 3, 4 };
        var wrap = PasswordWrap.Create(secret, "Centre2026!", 1_000);
        Assert.Equal(secret, wrap.TryUnwrap("Centre2026!"));
        Assert.Null(wrap.TryUnwrap("centre2026!"));
    }

    [Fact]
    public void File_storage_blocks_path_traversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "cs-fs-" + Guid.NewGuid().ToString("N"));
        var fs = new FileStorage(new AppPaths(new StorageOptions { DataFolder = root }));
        var p = fs.GetPath(@"..\..\windows\system32\evil.dll", StorageAreas.Documents);
        Assert.StartsWith(Path.Combine(root, "Documents"), p);
        Directory.Delete(root, true);
    }
}

public class PreferencesTests
{
    [Fact]
    public async Task Preferences_persist_and_survive_a_corrupt_file()
    {
        await using var host = await TestHost.CreateAsync();
        var prefs = host.Get<IUserPreferences>();
        Assert.Equal(1.0, prefs.Get(PreferenceKeys.TextScale, 1.0));
        prefs.Set(PreferenceKeys.TextScale, 1.15);
        prefs.Set(PreferenceKeys.OnboardingDismissed, true);
        var reloaded = new CentreSoutien.Infrastructure.Storage.JsonUserPreferences(host.Get<AppPaths>());
        Assert.Equal(1.15, reloaded.Get(PreferenceKeys.TextScale, 1.0));
        Assert.True(reloaded.Get(PreferenceKeys.OnboardingDismissed, false));

        File.WriteAllText(Path.Combine(host.Get<AppPaths>().Root, "preferences.json"), "{ not json");
        var broken = new CentreSoutien.Infrastructure.Storage.JsonUserPreferences(host.Get<AppPaths>());
        Assert.Equal(1.0, broken.Get(PreferenceKeys.TextScale, 1.0));
    }
}
