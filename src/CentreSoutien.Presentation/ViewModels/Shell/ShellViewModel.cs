using System.Collections.ObjectModel;
using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Presentation.ViewModels.Shell;

public sealed partial class NavItem(string key, string label, Type page, Func<NavItem, Task> go) : ObservableObject
{
    public string Key => key;
    public string Label => label;
    public Type Page => page;

    [ObservableProperty]
    private bool _isActive;

    [RelayCommand]
    private Task Go() => go(this);
}

public sealed class NavGroup(string? label, IReadOnlyList<NavItem> items)
{
    public string? Label => label;
    /// <summary>Uppercase caption as in the design (WPF has no text-transform).</summary>
    public string? Caption => label?.ToUpperInvariant();
    public bool HasLabel => !string.IsNullOrEmpty(label);
    public IReadOnlyList<NavItem> Items => items;
}

/// <summary>
/// Root view model: decides between login, forced password change, lock screen and the application,
/// and owns the sidebar, header, current page, dialog overlay and toast.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private readonly IThemeService _theme;
    private readonly ISettingsService _settings;
    private readonly TimeProvider _clock;
    private readonly IFileStorage _storage;

    public ShellViewModel(AppSession session, Navigator navigator, DialogHost dialogs, Notifier notifier, IThemeService theme,
        ISettingsService settings, IFileStorage storage, TimeProvider clock, LoginViewModel login, LockViewModel lockScreen, ChangePasswordViewModel changePassword)
    {
        _session = session;
        _theme = theme;
        _settings = settings;
        _clock = clock;
        _storage = storage;
        Navigator = navigator;
        Dialogs = dialogs;
        Notifier = notifier;
        Login = login;
        Lock = lockScreen;
        ChangePassword = changePassword;

        NavGroups =
        [
            new(null, [Item("dashboard", "Tableau de bord", typeof(DashboardViewModel))]),
            new("Scolarité",
            [
                Item("students", "Élèves", typeof(StudentsViewModel)),
                Item("parents", "Parents", typeof(ParentsViewModel)),
                Item("teachers", "Enseignants", typeof(TeachersViewModel)),
                Item("subjects", "Matières", typeof(SubjectsViewModel)),
                Item("courses", "Cours", typeof(CoursesViewModel)),
                Item("groups", "Groupes", typeof(GroupsViewModel)),
                Item("rooms", "Salles", typeof(RoomsViewModel)),
            ]),
            new("Planning",
            [
                Item("schedule", "Emploi du temps", typeof(ScheduleViewModel)),
                Item("sessions", "Séances", typeof(SessionsViewModel)),
                Item("attendance", "Présences", typeof(AttendanceViewModel)),
            ]),
            new("Évaluation",
            [
                Item("grades", "Notes", typeof(GradesViewModel)),
                Item("exams", "Examens", typeof(ExamsViewModel)),
            ]),
            new("Finances",
            [
                Item("payments", "Paiements", typeof(PaymentsViewModel)),
                Item("tpayments", "Paiements enseignants", typeof(TeacherPaymentsViewModel)),
                Item("expenses", "Dépenses", typeof(ExpensesViewModel)),
            ]),
            new("Centre",
            [
                Item("reports", "Rapports", typeof(ReportsViewModel)),
                Item("documents", "Documents", typeof(DocumentsViewModel)),
                Item("settings", "Paramètres", typeof(SettingsViewModel)),
            ]),
        ];

        _session.StateChanged += (_, _) => OnSessionChanged();
        Dialogs.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DialogHost.Current)) OnPropertyChanged(nameof(IsAppInteractive)); };
        Navigator.Navigated += (_, _) => HighlightNav();
        Login.SignedIn += async (_, _) => await EnterAppAsync();
        ChangePassword.Completed += async (_, _) => await EnterAppAsync();
    }

    public Navigator Navigator { get; }
    public DialogHost Dialogs { get; }
    public Notifier Notifier { get; }
    public LoginViewModel Login { get; }
    public LockViewModel Lock { get; }
    public ChangePasswordViewModel ChangePassword { get; }
    public IReadOnlyList<NavGroup> NavGroups { get; }

    public bool ShowLogin => _session.State == SessionState.SignedOut;
    public bool ShowChangePassword => _session.State == SessionState.PasswordChangeRequired;
    public bool ShowApp => _session.State is SessionState.Active or SessionState.Locked;
    public bool ShowLock => _session.State == SessionState.Locked;
    /// <summary>False while locked or while a dialog is open, so keyboard focus cannot reach hidden controls.</summary>
    public bool IsAppInteractive => !ShowLock && Dialogs.Current is null;

    public string OwnerName => string.IsNullOrWhiteSpace(_session.Account?.FullName) ? _session.Account?.Username ?? "" : _session.Account!.FullName;
    public string OwnerInitials => _session.Account?.Initials ?? "";

    [ObservableProperty]
    private string _centerName = "Centre de soutien";

    /// <summary>Full path of the custom logo, or null to use the bundled one.</summary>
    [ObservableProperty]
    private string? _logoPath;

    public bool HasCustomLogo => LogoPath is not null;
    partial void OnLogoPathChanged(string? value) => OnPropertyChanged(nameof(HasCustomLogo));

    public string? OwnerPhotoPath => _session.Account?.PhotoFile is { } f ? _storage.GetPath(f, StorageAreas.Images) : null;

    [ObservableProperty]
    private string _clockLabel = "";

    [ObservableProperty]
    private string _searchText = "";

    public bool IsDark => _theme.Current == AppTheme.Dark;
    public string ThemeLabel => IsDark ? "Mode clair" : "Mode sombre";

    /// <summary>Loads settings that the shell displays (center name, theme, lock delay).</summary>
    public async Task InitializeAsync()
    {
        await RunAsync(async () =>
        {
            var s = await _settings.GetAsync();
            ApplySettings(s);
            _theme.Apply(s.Theme);
            if (_session.State == SessionState.SignedOut) await Login.CheckFirstRunAsync();
            Refresh();
        });
    }

    public void ApplySettings(Domain.Entities.CenterSettings s)
    {
        CenterName = s.CenterName;
        LogoPath = s.LogoFile is null ? null : _storage.GetPath(s.LogoFile, StorageAreas.Images);
        _session.AutoLockMinutes = s.AutoLockMinutes;
        _session.SessionTimeoutMinutes = s.SessionTimeoutMinutes;
        Login.AutoLockMinutes = s.AutoLockMinutes;
    }

    public void UpdateClock()
    {
        var now = _clock.GetLocalNow().DateTime;
        ClockLabel = $"{Labels.LongDate(now)} · {now:HH:mm}";
    }

    private NavItem Item(string key, string label, Type page) => new(key, label, page, item => Navigator.NavigateAsync(item.Page));

    private void HighlightNav()
    {
        var key = Navigator.Current?.NavKey;
        foreach (var item in NavGroups.SelectMany(g => g.Items)) item.IsActive = item.Key == key;
    }

    private async Task EnterAppAsync()
    {
        if (_session.State != SessionState.Active) return;
        await InitializeAsync();
        if (Navigator.Current is null) await Navigator.NavigateAsync<DashboardViewModel>();
    }

    private void OnSessionChanged()
    {
        if (_session.State == SessionState.SignedOut)
        {
            Dialogs.CloseAll();
            Navigator.Reset();
            _ = Login.CheckFirstRunAsync();
        }
        if (_session.State == SessionState.Locked) Lock.Reset();
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(ShowLogin));
        OnPropertyChanged(nameof(ShowChangePassword));
        OnPropertyChanged(nameof(ShowApp));
        OnPropertyChanged(nameof(ShowLock));
        OnPropertyChanged(nameof(IsAppInteractive));
        OnPropertyChanged(nameof(OwnerName));
        OnPropertyChanged(nameof(OwnerInitials));
        OnPropertyChanged(nameof(OwnerPhotoPath));
        OnPropertyChanged(nameof(IsDark));
        OnPropertyChanged(nameof(ThemeLabel));
    }

    [RelayCommand]
    private async Task ToggleTheme()
    {
        var next = IsDark ? AppTheme.Light : AppTheme.Dark;
        _theme.Apply(next);
        Refresh();
        await RunAsync(async () =>
        {
            var s = await _settings.GetAsync();
            s.Theme = next;
            await _settings.SaveAsync(s);
        });
    }

    [RelayCommand]
    private void LockNow() => _session.Lock();

    [RelayCommand]
    private async Task SignOut()
    {
        if (!await Dialogs.ConfirmAsync("Se déconnecter", "Fermer la session du propriétaire ? Il faudra saisir à nouveau l'identifiant et le mot de passe.", "Se déconnecter", danger: false))
            return;
        _session.SignOut();
    }

    [RelayCommand]
    private Task OpenAccount() => Navigator.NavigateAsync<AccountViewModel>();

    [RelayCommand]
    private Task Back() => Navigator.BackAsync();

    [RelayCommand]
    private async Task Search()
    {
        var q = SearchText.Trim();
        SearchText = "";
        await Navigator.NavigateAsync<StudentsViewModel>(new StudentsViewModel.Query(q));
    }
}
