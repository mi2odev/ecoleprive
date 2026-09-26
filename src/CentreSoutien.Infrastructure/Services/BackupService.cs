using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CentreSoutien.Application.Abstractions;
using CentreSoutien.Infrastructure.Data;
using CentreSoutien.Infrastructure.Security;
using CentreSoutien.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Services;

/// <summary>
/// Backup file (.csbak) = magic + JSON header + AES-256-GCM encrypted zip (database copy, images, documents).
/// The payload key is the database key; the header carries that key wrapped with the owner's password,
/// so a backup can be restored on a new computer with the password that was in use when it was made.
/// </summary>
public sealed class BackupService(
    IDbContextFactory<AppDbContext> factory,
    AppPaths paths,
    KeyStore keys,
    StorageOptions options,
    ConnectionStringProvider connections,
    TimeProvider clock) : IBackupService
{
    private static readonly byte[] Magic = "CSBAK1\n"u8.ToArray();
    public const string Extension = ".csbak";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private sealed record Header(DateTime CreatedAt, string CenterName, string AppVersion, bool DatabaseEncrypted, PasswordWrap Wrap, string Nonce, string Tag);

    public string DefaultFolder => paths.Backups;

    public async Task<string> BackupAsync(string? folder = null, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var wrap = keys.BackupWrap ?? throw new BusinessException("Connectez-vous une fois avec votre mot de passe avant de sauvegarder.");
            await using var db = await factory.CreateDbContextAsync(ct);
            var settings = await db.Settings.FirstAsync(ct);
            folder = string.IsNullOrWhiteSpace(folder) ? (string.IsNullOrWhiteSpace(settings.BackupFolder) ? paths.Backups : settings.BackupFolder) : folder;
            Directory.CreateDirectory(folder);

            var work = Directory.CreateTempSubdirectory("csbak-");
            try
            {
                var dbCopy = Path.Combine(work.FullName, "database.db");
                var passphrase = options.EncryptDatabase ? keys.Passphrase : null;
                using (var src = new SqliteConnection(connections.ConnectionString))
                using (var dst = new SqliteConnection(ConnectionStringProvider.For(dbCopy, passphrase, pooling: false)))
                {
                    src.Open();
                    dst.Open();
                    src.BackupDatabase(dst);
                }

                byte[] zip;
                using (var ms = new MemoryStream())
                {
                    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        archive.CreateEntryFromFile(dbCopy, "database.db", CompressionLevel.Optimal);
                        AddFolder(archive, paths.Images, "images/");
                        AddFolder(archive, paths.Documents, "documents/");
                    }
                    zip = ms.ToArray();
                }

                var nonce = RandomNumberGenerator.GetBytes(12);
                var tag = new byte[16];
                var cipher = new byte[zip.Length];
                using (var aes = new AesGcm(keys.DatabaseKey, 16)) aes.Encrypt(nonce, zip, cipher, tag);

                var now = clock.GetLocalNow().DateTime;
                var header = new Header(now, settings.CenterName, typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "1.0",
                    options.EncryptDatabase, wrap, Convert.ToBase64String(nonce), Convert.ToBase64String(tag));
                var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header);

                var file = Path.Combine(folder, $"sauvegarde-{now:yyyy-MM-dd-HHmmss}{Extension}");
                await using (var fs = File.Create(file))
                {
                    await fs.WriteAsync(Magic, ct);
                    await fs.WriteAsync(BitConverter.GetBytes(headerBytes.Length), ct);
                    await fs.WriteAsync(headerBytes, ct);
                    await fs.WriteAsync(cipher, ct);
                }

                settings.LastBackupAt = now;
                settings.LastBackupSize = new FileInfo(file).Length;
                await db.SaveChangesAsync(ct);
                Prune(folder, settings.BackupRetentionCount);
                return file;
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { work.Delete(true); } catch (IOException) { }
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task RestoreAsync(string backupFile, string password, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        var work = Directory.CreateTempSubdirectory("csrestore-");
        try
        {
            var (header, cipher) = Read(backupFile);
            var key = header.Wrap.TryUnwrap(password ?? "")
                ?? throw new BusinessException("Mot de passe incorrect pour cette sauvegarde (utilisez le mot de passe en vigueur au moment de la sauvegarde).");

            var zip = new byte[cipher.Length];
            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(Convert.FromBase64String(header.Nonce), cipher, Convert.FromBase64String(header.Tag), zip);
            }
            catch (AuthenticationTagMismatchException)
            {
                throw new BusinessException("Le fichier de sauvegarde est endommagé ou a été modifié.");
            }

            using (var ms = new MemoryStream(zip))
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
                archive.ExtractToDirectory(work.FullName);

            var restoredDb = Path.Combine(work.FullName, "database.db");
            if (!File.Exists(restoredDb)) throw new BusinessException("La sauvegarde ne contient pas de base de données.");

            // Re-encrypt the restored copy with this computer's key (or decrypt it if encryption is off).
            var target = Path.Combine(work.FullName, "target.db");
            var targetPass = options.EncryptDatabase ? keys.Passphrase : "";
            using (var conn = new SqliteConnection(ConnectionStringProvider.For(restoredDb, header.DatabaseEncrypted ? KeyStore.PassphraseFor(key) : null, pooling: false)))
            {
                conn.Open();
                using var check = conn.CreateCommand();
                check.CommandText = "PRAGMA integrity_check;";
                if (check.ExecuteScalar() as string != "ok") throw new BusinessException("La base de données de la sauvegarde est corrompue.");
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "ATTACH DATABASE $path AS target KEY $key; SELECT sqlcipher_export('target'); DETACH DATABASE target;";
                cmd.Parameters.AddWithValue("$path", target);
                cmd.Parameters.AddWithValue("$key", targetPass);
                cmd.ExecuteNonQuery();
            }

            // Swap the live database; keep the previous one next to the backups just in case.
            SqliteConnection.ClearAllPools();
            var safety = Path.Combine(paths.Backups, $"avant-restauration-{clock.GetLocalNow():yyyy-MM-dd-HHmmss}.db");
            if (File.Exists(paths.Database)) File.Copy(paths.Database, safety, overwrite: true);
            foreach (var suffix in new[] { "-wal", "-shm" })
                if (File.Exists(paths.Database + suffix)) File.Delete(paths.Database + suffix);
            File.Copy(target, paths.Database, overwrite: true);

            CopyFolder(Path.Combine(work.FullName, "images"), paths.Images);
            CopyFolder(Path.Combine(work.FullName, "documents"), paths.Documents);

            // Bring an older backup up to the current schema.
            await using var db = await factory.CreateDbContextAsync(ct);
            await db.Database.MigrateAsync(ct);
            keys.SetBackupPassword(password!);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { work.Delete(true); } catch (IOException) { }
            Gate.Release();
        }
    }

    public async Task<bool> RunScheduledAsync(DateTime now, CancellationToken ct = default)
    {
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            var s = await db.Settings.AsNoTracking().FirstAsync(ct);
            if (!s.AutoBackupEnabled) return false;
            // Most recent scheduled moment; catches up the next time the app runs if the PC was off.
            var due = now.Date + s.AutoBackupTime;
            if (due > now) due = due.AddDays(-1);
            if (s.LastBackupAt is { } last && last >= due) return false;
        }
        await BackupAsync(null, ct);
        return true;
    }

    private static (Header Header, byte[] Cipher) Read(string file)
    {
        if (!File.Exists(file)) throw new BusinessException("Fichier de sauvegarde introuvable.");
        var bytes = File.ReadAllBytes(file);
        if (bytes.Length < Magic.Length + 4 || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new BusinessException("Ce fichier n'est pas une sauvegarde du centre.");
        var len = BitConverter.ToInt32(bytes, Magic.Length);
        var start = Magic.Length + 4;
        if (len <= 0 || start + len > bytes.Length) throw new BusinessException("En-tête de sauvegarde invalide.");
        var header = JsonSerializer.Deserialize<Header>(Encoding.UTF8.GetString(bytes, start, len)) ?? throw new BusinessException("En-tête de sauvegarde invalide.");
        return (header, bytes[(start + len)..]);
    }

    private static void AddFolder(ZipArchive archive, string folder, string prefix)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var f in Directory.EnumerateFiles(folder))
            archive.CreateEntryFromFile(f, prefix + Path.GetFileName(f), CompressionLevel.Fastest);
    }

    private static void CopyFolder(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from))
            File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: true);
    }

    private static void Prune(string folder, int keep)
    {
        if (keep <= 0) return;
        var files = new DirectoryInfo(folder).GetFiles("sauvegarde-*" + Extension).OrderByDescending(f => f.Name).Skip(keep);
        foreach (var f in files)
        {
            try { f.Delete(); } catch (IOException) { }
        }
    }
}
