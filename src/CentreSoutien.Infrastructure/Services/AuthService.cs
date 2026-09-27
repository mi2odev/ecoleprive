using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Infrastructure.Data;
using CentreSoutien.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Services;

public sealed class AuthService(IDbContextFactory<AppDbContext> factory, IPasswordHasher hasher, KeyStore keys, TimeProvider clock) : IAuthService
{
    /// <summary>Failed attempts allowed before a temporary lockout.</summary>
    public const int MaxAttempts = 5;

    public async Task<LoginResult> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var account = await db.Accounts.SingleAsync(ct);
        var result = Check(account, password, username);
        if (result.Succeeded)
        {
            account.LastLoginAt = clock.GetLocalNow().DateTime;
            if (hasher.NeedsRehash(account.PasswordHash)) account.PasswordHash = hasher.Hash(password);
            if (keys.BackupWrap is null) keys.SetBackupPassword(password);
            db.AuditLog.Add(Audit.Entry(clock, Domain.Enums.AuditCategory.Security, "Connexion"));
        }
        else
            db.AuditLog.Add(Audit.Entry(clock, Domain.Enums.AuditCategory.Security,
                result.Outcome == LoginOutcome.LockedOut ? "Connexion bloquée (trop d'essais)" : "Échec de connexion",
                string.IsNullOrWhiteSpace(username) ? null : $"Nom d'utilisateur saisi : {username.Trim()}"));
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<LoginResult> VerifyPasswordAsync(string password, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var account = await db.Accounts.SingleAsync(ct);
        var result = Check(account, password, null);
        await db.SaveChangesAsync(ct);
        return result;
    }

    private LoginResult Check(OwnerAccount account, string password, string? username)
    {
        var now = clock.GetLocalNow().DateTime;
        if (account.LockedOutUntil is { } until && until > now)
            return new(LoginOutcome.LockedOut, RetryAfter: until - now);

        // Always run the hash, even for a wrong username, so timing does not reveal which part was wrong.
        var passwordOk = hasher.Verify(password ?? "", account.PasswordHash);
        var userOk = username is null || string.Equals(username.Trim(), account.Username, StringComparison.OrdinalIgnoreCase);
        if (passwordOk && userOk)
        {
            account.FailedLoginCount = 0;
            account.LockedOutUntil = null;
            return new(LoginOutcome.Success, account);
        }

        account.FailedLoginCount++;
        if (account.FailedLoginCount >= MaxAttempts)
        {
            // 1, 2, 4, 8… minutes, capped at 15.
            var exp = Math.Min(4, account.FailedLoginCount - MaxAttempts);
            var wait = TimeSpan.FromMinutes(Math.Min(15, 1 << exp));
            account.LockedOutUntil = now + wait;
            return new(LoginOutcome.LockedOut, RetryAfter: wait);
        }
        return new(LoginOutcome.InvalidCredentials);
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var account = await db.Accounts.SingleAsync(ct);
        if (!hasher.Verify(currentPassword ?? "", account.PasswordHash))
            throw new BusinessException("Le mot de passe actuel est incorrect.");
        if (PasswordPolicy.Validate(newPassword, account.Username) is { } error)
            throw new BusinessException(error);
        if (hasher.Verify(newPassword, account.PasswordHash))
            throw new BusinessException("Le nouveau mot de passe doit être différent de l'ancien.");

        account.PasswordHash = hasher.Hash(newPassword);
        account.MustChangePassword = false;
        account.PasswordChangedAt = clock.GetLocalNow().DateTime;
        db.AuditLog.Add(Audit.Entry(clock, Domain.Enums.AuditCategory.Security, "Mot de passe modifié"));
        await db.SaveChangesAsync(ct);
        keys.SetBackupPassword(newPassword);
    }

    public async Task<OwnerAccount> GetAccountAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Accounts.AsNoTracking().SingleAsync(ct);
    }

    public async Task<OwnerAccount> UpdateProfileAsync(string username, string fullName, string? email, string? phone, string? photoFile, CancellationToken ct = default)
    {
        username = (username ?? "").Trim();
        if (username.Length < 3 || username.Length > 64 || username.Any(char.IsWhiteSpace))
            throw new BusinessException("Le nom d'utilisateur doit contenir 3 à 64 caractères, sans espace.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var account = await db.Accounts.SingleAsync(ct);
        account.Username = username;
        account.FullName = (fullName ?? "").Trim();
        account.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        account.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        account.PhotoFile = photoFile;
        await db.SaveChangesAsync(ct);
        return account;
    }
}
