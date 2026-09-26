using System.Security.Cryptography;
using CentreSoutien.Application.Abstractions;

namespace CentreSoutien.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 with a random 128-bit salt (OWASP 2023+ recommends 600 000 iterations).
/// Format: <c>pbkdf2-sha256$iterations$salt$hash</c> (base64).
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Scheme = "pbkdf2-sha256";
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private readonly int _iterations;

    public Pbkdf2PasswordHasher(int iterations = 600_000) => _iterations = iterations;

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Scheme}${_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string stored)
    {
        if (password is null || string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations) || iterations < 1) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool NeedsRehash(string stored)
    {
        var parts = stored.Split('$');
        return parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var it) || it < _iterations;
    }
}
