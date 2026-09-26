using CentreSoutien.Domain.Entities;

namespace CentreSoutien.Application.Abstractions;

public enum SessionState
{
    SignedOut,
    /// <summary>Signed in with the default password: only the password change screen is available.</summary>
    PasswordChangeRequired,
    Active,
    Locked,
}

/// <summary>
/// In-memory state of the owner's session: signed in, locked by inactivity, or signed out.
/// Pure state machine without UI or timer dependencies so it can be unit tested.
/// </summary>
public sealed class AppSession
{
    private readonly TimeProvider _clock;

    public AppSession(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public SessionState State { get; private set; } = SessionState.SignedOut;
    public OwnerAccount? Account { get; private set; }
    public DateTimeOffset? SignedInAt { get; private set; }
    public DateTimeOffset LastActivity { get; private set; }
    public DateTimeOffset? LockedAt { get; private set; }

    public int AutoLockMinutes { get; set; } = 10;
    public int SessionTimeoutMinutes { get; set; } = 120;

    public event EventHandler? StateChanged;

    public bool IsSignedIn => State is SessionState.Active or SessionState.Locked or SessionState.PasswordChangeRequired;

    public void SignIn(OwnerAccount account)
    {
        Account = account;
        SignedInAt = _clock.GetUtcNow();
        LastActivity = SignedInAt.Value;
        LockedAt = null;
        SetState(account.MustChangePassword ? SessionState.PasswordChangeRequired : SessionState.Active);
    }

    public void PasswordChanged(OwnerAccount account)
    {
        Account = account;
        if (State == SessionState.PasswordChangeRequired) SetState(SessionState.Active);
    }

    public void UpdateAccount(OwnerAccount account)
    {
        Account = account;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Touch()
    {
        if (State == SessionState.Active) LastActivity = _clock.GetUtcNow();
    }

    public void Lock()
    {
        if (State is not (SessionState.Active or SessionState.PasswordChangeRequired)) return;
        LockedAt = _clock.GetUtcNow();
        SetState(SessionState.Locked);
    }

    public void Unlock()
    {
        if (State != SessionState.Locked) return;
        LockedAt = null;
        LastActivity = _clock.GetUtcNow();
        SetState(Account?.MustChangePassword == true ? SessionState.PasswordChangeRequired : SessionState.Active);
    }

    public void SignOut()
    {
        Account = null;
        SignedInAt = null;
        LockedAt = null;
        SetState(SessionState.SignedOut);
    }

    /// <summary>Called periodically: locks after inactivity and ends a session left locked too long.</summary>
    public void Tick()
    {
        var now = _clock.GetUtcNow();
        if ((State is SessionState.Active or SessionState.PasswordChangeRequired) && AutoLockMinutes > 0 && now - LastActivity >= TimeSpan.FromMinutes(AutoLockMinutes))
            Lock();
        else if (State == SessionState.Locked && SessionTimeoutMinutes > 0 && LockedAt is { } at && now - at >= TimeSpan.FromMinutes(SessionTimeoutMinutes))
            SignOut();
    }

    private void SetState(SessionState s)
    {
        State = s;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
