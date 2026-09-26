using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using CentreSoutien.Infrastructure.Storage;

namespace CentreSoutien.Infrastructure.Security;

/// <summary>Protects the database key at rest on this computer.</summary>
public interface IKeyProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
    string Name { get; }
}

/// <summary>Windows DPAPI, bound to the current Windows user account.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiKeyProtector : IKeyProtector
{
    private static readonly byte[] Entropy = "CentreSoutien.DbKey.v1"u8.ToArray();
    public string Name => "dpapi";
    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}

/// <summary>No OS protection (non-Windows development and tests only).</summary>
public sealed class PlainKeyProtector : IKeyProtector
{
    public string Name => "plain";
    public byte[] Protect(byte[] data) => data;
    public byte[] Unprotect(byte[] data) => data;
}

/// <summary>AES-GCM blob encrypted with a key derived from the owner's password (PBKDF2-SHA256).</summary>
public sealed record PasswordWrap(string Salt, int Iterations, string Nonce, string Tag, string Cipher)
{
    public const int DefaultIterations = 600_000;

    public static PasswordWrap Create(byte[] secret, string password, int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var kek = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[secret.Length];
        using (var aes = new AesGcm(kek, 16)) aes.Encrypt(nonce, secret, cipher, tag);
        CryptographicOperations.ZeroMemory(kek);
        return new(Convert.ToBase64String(salt), iterations, Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(cipher));
    }

    /// <summary>Returns null when the password is wrong.</summary>
    public byte[]? TryUnwrap(string password)
    {
        var kek = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(Salt), Iterations, HashAlgorithmName.SHA256, 32);
        var cipher = Convert.FromBase64String(Cipher);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(kek, 16);
            aes.Decrypt(Convert.FromBase64String(Nonce), cipher, Convert.FromBase64String(Tag), plain);
            return plain;
        }
        catch (AuthenticationTagMismatchException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }
}

/// <summary>
/// Holds the random 256-bit SQLCipher key. It is stored in keys.json protected by DPAPI, plus a copy
/// wrapped with the owner's password so that encrypted backups can be restored on another computer.
/// </summary>
public sealed class KeyStore
{
    private sealed class KeyFileModel
    {
        public int Version { get; set; } = 1;
        public string Protector { get; set; } = "";
        public string ProtectedKey { get; set; } = "";
        public PasswordWrap? BackupWrap { get; set; }
    }

    private readonly AppPaths _paths;
    private readonly IKeyProtector _protector;
    private readonly object _gate = new();
    private byte[]? _key;

    public KeyStore(AppPaths paths, IKeyProtector protector)
    {
        _paths = paths;
        _protector = protector;
    }

    public byte[] DatabaseKey
    {
        get
        {
            lock (_gate)
            {
                if (_key is not null) return _key;
                var model = Load();
                if (model is null)
                {
                    _key = RandomNumberGenerator.GetBytes(32);
                    Save(new KeyFileModel { Protector = _protector.Name, ProtectedKey = Convert.ToBase64String(_protector.Protect(_key)) });
                }
                else
                {
                    if (model.Protector != _protector.Name)
                        throw new InvalidOperationException($"La clé de la base a été protégée avec « {model.Protector} » et ne peut pas être lue ici.");
                    _key = _protector.Unprotect(Convert.FromBase64String(model.ProtectedKey));
                }
                return _key;
            }
        }
    }

    /// <summary>SQLCipher passphrase (hex of the random key), passed through the connection string Password keyword.</summary>
    public string Passphrase => PassphraseFor(DatabaseKey);

    public PasswordWrap? BackupWrap => Load()?.BackupWrap;

    /// <summary>Re-wraps the database key with the owner's (new) password.</summary>
    public void SetBackupPassword(string password)
    {
        lock (_gate)
        {
            var key = DatabaseKey;
            var model = Load() ?? throw new InvalidOperationException("keys.json manquant");
            model.BackupWrap = PasswordWrap.Create(key, password);
            Save(model);
        }
    }

    public static string PassphraseFor(byte[] key) => Convert.ToHexString(key);

    private KeyFileModel? Load() =>
        File.Exists(_paths.KeyFile) ? JsonSerializer.Deserialize<KeyFileModel>(File.ReadAllText(_paths.KeyFile)) : null;

    private void Save(KeyFileModel model)
    {
        var tmp = _paths.KeyFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _paths.KeyFile, overwrite: true);
    }
}
