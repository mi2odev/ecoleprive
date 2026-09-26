using CentreSoutien.Application.Abstractions;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Presentation.ViewModels.Pages;

/// <summary>The single owner account: profile, password and current session.</summary>
public sealed partial class AccountViewModel(
    IAuthService auth, AppSession session, ShellViewModel shell, IFileStorage storage, IFilePicker files,
    DialogHost dialogs, INotifier notifier, TimeProvider clock) : PageViewModel
{
    public override string NavKey => "account";
    public override string Title => "Mon compte";

    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _photoFile;
    [ObservableProperty] private string? _photoPath;
    [ObservableProperty] private string _initials = "";

    [ObservableProperty] private string _currentPassword = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _confirmPassword = "";
    [ObservableProperty] private string? _passwordError;

    [ObservableProperty] private string _sessionLabel = "";
    [ObservableProperty] private string _passwordChangedLabel = "";

    public string PasswordRules => $"{PasswordPolicy.MinLength} caractères minimum, avec des lettres et des chiffres ou symboles. Le mot de passe est stocké haché, jamais en clair.";
    public bool HasPhoto => PhotoPath is not null;
    public bool HasPasswordError => !string.IsNullOrEmpty(PasswordError);

    partial void OnPhotoPathChanged(string? value) => OnPropertyChanged(nameof(HasPhoto));
    partial void OnPasswordErrorChanged(string? value) => OnPropertyChanged(nameof(HasPasswordError));

    public override async Task LoadAsync(object? parameter)
    {
        await RunAsync(async () =>
        {
            var a = await auth.GetAccountAsync();
            Username = a.Username;
            FullName = a.FullName;
            Email = a.Email;
            Phone = a.Phone;
            PhotoFile = a.PhotoFile;
            PhotoPath = a.PhotoFile is null ? null : storage.GetPath(a.PhotoFile, StorageAreas.Images);
            Initials = a.Initials;
            PasswordChangedLabel = a.PasswordChangedAt is { } at ? $"Dernière modification le {at:dd/MM/yyyy}" : "";
            UpdateSessionLabel();
        }, notifier);
    }

    private void UpdateSessionLabel()
    {
        if (session.SignedInAt is not { } at)
        {
            SessionLabel = "Session inactive";
            return;
        }
        var local = TimeZoneInfo.ConvertTime(at, clock.LocalTimeZone);
        var today = clock.GetLocalNow().Date;
        SessionLabel = local.Date == today
            ? $"Connecté depuis {local:HH:mm} sur ce poste"
            : $"Connecté depuis le {local:dd/MM/yyyy} à {local:HH:mm} sur ce poste";
    }

    [RelayCommand]
    private void ChangePhoto()
    {
        var path = files.OpenFile("Choisir une photo", "Images|*.jpg;*.jpeg;*.png;*.bmp");
        if (path is null) return;
        try
        {
            PhotoFile = storage.Import(path, StorageAreas.Images);
            PhotoPath = storage.GetPath(PhotoFile, StorageAreas.Images);
        }
        catch (BusinessException ex)
        {
            notifier.Error(ex.Message);
        }
    }

    [RelayCommand]
    private void RemovePhoto()
    {
        PhotoFile = null;
        PhotoPath = null;
    }

    [RelayCommand]
    private async Task SaveProfile()
    {
        if (IsBusy) return;
        await RunAsync(async () =>
        {
            await auth.UpdateProfileAsync(Username, FullName, Email, Phone, PhotoFile);
            var account = await auth.GetAccountAsync();
            session.UpdateAccount(account);
            shell.Refresh();
            Username = account.Username;
            Initials = account.Initials;
            notifier.Info("Profil enregistré");
        }, notifier);
    }

    [RelayCommand]
    private async Task ChangePassword()
    {
        if (IsBusy) return;
        PasswordError = null;
        if (string.IsNullOrEmpty(CurrentPassword)) { PasswordError = "Saisissez votre mot de passe actuel."; return; }
        if (NewPassword != ConfirmPassword) { PasswordError = "La confirmation ne correspond pas au nouveau mot de passe."; return; }
        var ok = await RunAsync(async () =>
        {
            await auth.ChangePasswordAsync(CurrentPassword, NewPassword);
            var account = await auth.GetAccountAsync();
            session.UpdateAccount(account);
            PasswordChangedLabel = account.PasswordChangedAt is { } at ? $"Dernière modification le {at:dd/MM/yyyy}" : "";
        });
        if (ok)
        {
            CurrentPassword = NewPassword = ConfirmPassword = "";
            notifier.Info("Mot de passe mis à jour");
        }
        else
        {
            PasswordError = Error;
            Error = null;
        }
    }

    [RelayCommand]
    private void Lock() => session.Lock();

    [RelayCommand]
    private async Task SignOut()
    {
        if (!await dialogs.ConfirmAsync("Se déconnecter", "Fermer la session du propriétaire ? Il faudra saisir à nouveau l'identifiant et le mot de passe.", "Se déconnecter", danger: false))
            return;
        session.SignOut();
    }
}
