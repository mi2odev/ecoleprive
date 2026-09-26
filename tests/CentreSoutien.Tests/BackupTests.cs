using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Infrastructure.Storage;

namespace CentreSoutien.Tests;

public class BackupTests
{
    [Fact]
    public async Task Backup_restores_on_another_computer_with_the_owner_password()
    {
        string backupFile;
        await using (var source = await TestHost.CreateAsync())
        {
            await source.Get<IAuthService>().ChangePasswordAsync("admin", "Centre2026!");
            await source.Get<IStudentService>().SaveAsync(new Student { FirstName = "Lina", LastName = "Kaci", Level = "4AM" });
            var photo = Path.Combine(source.Folder, "photo.jpg");
            await File.WriteAllBytesAsync(photo, [1, 2, 3]);
            var stored = source.Get<IFileStorage>().Import(photo, StorageAreas.Images);

            backupFile = await source.Get<IBackupService>().BackupAsync(Path.GetTempPath());
            Assert.True(File.Exists(backupFile));
            var raw = await File.ReadAllBytesAsync(backupFile);
            Assert.DoesNotContain("Kaci", System.Text.Encoding.UTF8.GetString(raw));
            Assert.NotNull((await source.Get<ISettingsService>().GetAsync()).LastBackupAt);

            // A different installation (its own random database key).
            await using var target = await TestHost.CreateAsync();
            await Assert.ThrowsAsync<BusinessException>(() => target.Get<IBackupService>().RestoreAsync(backupFile, "admin"));
            await target.Get<IBackupService>().RestoreAsync(backupFile, "Centre2026!");

            var students = await target.Get<IStudentService>().ListAsync(DateTime.Today);
            Assert.Single(students);
            Assert.Equal("Lina Kaci", students[0].FullName);
            Assert.True((await target.Get<IAuthService>().LoginAsync("admin", "Centre2026!")).Succeeded);
            Assert.True(File.Exists(target.Get<IFileStorage>().GetPath(stored, StorageAreas.Images)));
        }
        File.Delete(backupFile);
    }

    [Fact]
    public async Task Scheduled_backup_runs_once_per_day_after_the_configured_time()
    {
        await using var host = await TestHost.CreateAsync();
        var settings = host.Get<ISettingsService>();
        var s = await settings.GetAsync();
        s.BackupFolder = Path.Combine(host.Folder, "auto");
        s.AutoBackupTime = new TimeSpan(22, 0, 0);
        await settings.SaveAsync(s);
        var backup = host.Get<IBackupService>();

        Assert.True(await backup.RunScheduledAsync(new DateTime(2026, 9, 26, 8, 0, 0))); // catches up yesterday's 22:00
        host.Clock.Now = new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.FromHours(1));
        await backup.BackupAsync(); // stamps LastBackupAt with the clock
        Assert.False(await backup.RunScheduledAsync(new DateTime(2026, 9, 26, 12, 0, 0)));
        host.Clock.Now = new DateTimeOffset(2026, 9, 26, 22, 5, 0, TimeSpan.FromHours(1));
        Assert.True(await backup.RunScheduledAsync(new DateTime(2026, 9, 26, 22, 5, 0)));
        Assert.False(await backup.RunScheduledAsync(new DateTime(2026, 9, 26, 23, 0, 0)));
    }
}
