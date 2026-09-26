using CentreSoutien.Domain.Entities;

namespace CentreSoutien.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
    /// <summary>True when the stored hash uses weaker parameters than the current policy.</summary>
    bool NeedsRehash(string hash);
}

public enum LoginOutcome
{
    Success,
    InvalidCredentials,
    LockedOut,
}

public sealed record LoginResult(LoginOutcome Outcome, OwnerAccount? Account = null, TimeSpan? RetryAfter = null)
{
    public bool Succeeded => Outcome == LoginOutcome.Success;
}

public interface IAuthService
{
    Task<LoginResult> LoginAsync(string username, string password, CancellationToken ct = default);
    /// <summary>Checks the owner's password (lock screen, sensitive actions). Applies the same throttling as login.</summary>
    Task<LoginResult> VerifyPasswordAsync(string password, CancellationToken ct = default);
    Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default);
    Task<OwnerAccount> GetAccountAsync(CancellationToken ct = default);
    Task<OwnerAccount> UpdateProfileAsync(string username, string fullName, string? email, string? phone, string? photoFile, CancellationToken ct = default);
}

public static class PasswordPolicy
{
    public const int MinLength = 8;

    /// <summary>Returns an error message in French, or null when the password is acceptable.</summary>
    public static string? Validate(string? password, string? username = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"Le mot de passe doit contenir au moins {MinLength} caractères.";
        if (!password.Any(char.IsLetter) || !password.Any(c => !char.IsLetter(c)))
            return "Le mot de passe doit mélanger lettres et chiffres ou symboles.";
        if (username is not null && string.Equals(password, username, StringComparison.OrdinalIgnoreCase))
            return "Le mot de passe ne peut pas être identique au nom d'utilisateur.";
        if (string.Equals(password, "admin", StringComparison.OrdinalIgnoreCase) || password == "12345678" || password == "password")
            return "Ce mot de passe est trop courant.";
        return null;
    }
}

/// <summary>Thrown for business rule violations; the message is shown to the owner as-is (French).</summary>
public sealed class BusinessException(string message) : Exception(message);
