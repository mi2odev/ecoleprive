using CentreSoutien.Application.Abstractions;
using CentreSoutien.Presentation.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Presentation.ViewModels.Shell;

internal static class AuthMessages
{
    public static string For(LoginResult r) => r.Outcome switch
    {
        LoginOutcome.LockedOut => $"Trop de tentatives. Réessayez dans {Math.Max(1, (int)Math.Ceiling(r.RetryAfter?.TotalMinutes ?? 1))} min.",
        _ => "Nom d'utilisateur ou mot de passe incorrect.",
    };
}

public sealed partial class LoginViewModel(IAuthService auth, AppSession session) : ViewModelBase
{
    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private int _autoLockMinutes = 10;

    /// <summary>True while the account still has the installation password (shows the first-use hint).</summary>
    [ObservableProperty]
    private bool _isFirstRun;

    public string LockHint => AutoLockMinutes > 0
        ? $"Verrouillage automatique après {AutoLockMinutes} min d'inactivité."
        : "Verrouillage automatique désactivé.";

    partial void OnAutoLockMinutesChanged(int value) => OnPropertyChanged(nameof(LockHint));

    public event EventHandler? SignedIn;

    public async Task CheckFirstRunAsync()
    {
        var account = await auth.GetAccountAsync();
        IsFirstRun = account.MustChangePassword;
        // Only reveal the username before the owner has personalised the account.
        if (IsFirstRun && string.IsNullOrEmpty(Username)) Username = account.Username;
    }

    [RelayCommand]
    private async Task SignIn()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(Username)) { Error = "Saisissez votre nom d'utilisateur."; return; }
        if (string.IsNullOrEmpty(Password)) { Error = "Saisissez votre mot de passe."; return; }
        await RunAsync(async () =>
        {
            var result = await auth.LoginAsync(Username, Password);
            Password = "";
            if (!result.Succeeded)
            {
                Error = AuthMessages.For(result);
                return;
            }
            session.SignIn(result.Account!);
            SignedIn?.Invoke(this, EventArgs.Empty);
        });
    }
}

public sealed partial class LockViewModel(IAuthService auth, AppSession session) : ViewModelBase
{
    [ObservableProperty]
    private string _password = "";

    public void Reset()
    {
        Password = "";
        Error = null;
    }

    [RelayCommand]
    private async Task Unlock()
    {
        if (IsBusy) return;
        if (string.IsNullOrEmpty(Password)) { Error = "Saisissez votre mot de passe."; return; }
        await RunAsync(async () =>
        {
            var result = await auth.VerifyPasswordAsync(Password);
            Password = "";
            if (!result.Succeeded)
            {
                Error = AuthMessages.For(result);
                return;
            }
            session.Unlock();
        });
    }

    [RelayCommand]
    private void SignOut() => session.SignOut();
}

/// <summary>Shown after signing in with the default password: the owner must choose a personal one.</summary>
public sealed partial class ChangePasswordViewModel(IAuthService auth, AppSession session) : ViewModelBase
{
    [ObservableProperty]
    private string _currentPassword = "";

    [ObservableProperty]
    private string _newPassword = "";

    [ObservableProperty]
    private string _confirmPassword = "";

    public string Rules => $"Au moins {PasswordPolicy.MinLength} caractères, avec des lettres et des chiffres ou symboles. Le mot de passe est stocké haché, jamais en clair.";

    public event EventHandler? Completed;

    [RelayCommand]
    private async Task Save()
    {
        if (IsBusy) return;
        if (NewPassword != ConfirmPassword) { Error = "La confirmation ne correspond pas au nouveau mot de passe."; return; }
        await RunAsync(async () =>
        {
            await auth.ChangePasswordAsync(CurrentPassword, NewPassword);
            CurrentPassword = NewPassword = ConfirmPassword = "";
            session.PasswordChanged(await auth.GetAccountAsync());
            Completed?.Invoke(this, EventArgs.Empty);
        });
    }

    [RelayCommand]
    private void SignOut() => session.SignOut();
}
