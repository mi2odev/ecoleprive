namespace CentreSoutien.Domain.Entities;

/// <summary>
/// The one and only account of the application: the owner of the center.
/// There are no roles or permissions — whoever is authenticated with this account has full access.
/// </summary>
public class OwnerAccount : Entity
{
    public string Username { get; set; } = "admin";

    /// <summary>Salted PBKDF2 hash (see IPasswordHasher). Never the plain password.</summary>
    public string PasswordHash { get; set; } = "";

    public bool MustChangePassword { get; set; }
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>File name inside the application's image storage folder.</summary>
    public string? PhotoFile { get; set; }

    public DateTime? LastLoginAt { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockedOutUntil { get; set; }

    public string Initials
    {
        get
        {
            var parts = FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return Username.Length > 0 ? Username[..1].ToUpperInvariant() : "?";
            return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
        }
    }
}
