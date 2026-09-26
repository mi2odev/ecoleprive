using System.Collections.ObjectModel;
using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Presentation.ViewModels.Shell;

public sealed partial class NavItem(string key, string label, Type page, Func<NavItem, Task> go, string glyph = "") : ObservableObject
{
    public string Key => key;
    public string Label => label;
    public Type Page => page;
    /// <summary>Icon of the sidebar entry: a character of the Segoe Fluent Icons / Segoe MDL2 Assets font.</summary>
    public string Glyph => glyph;
    /// <summary>Ctrl+digit shortcut of the entry ("Ctrl+2"), or null.</summary>
    public string? Shortcut { get; init; }
    public string ToolTip => Shortcut is null ? label : $"{label} ({Shortcut})";

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

/// <summary>One choice of the text size control (Normal / Grand / Très grand).</summary>
public sealed partial class TextScaleOption(string label, double value, Action<TextScaleOption> select) : ObservableObject
{
    public string Label => label;
    public double Value => value;

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => select(this);
}

/// <summary>Icon characters (Segoe Fluent Icons, same code points as Segoe MDL2 Assets) used by the shell.</summary>
public static class Glyphs
{
    public const string Home = "";
    public const string People = "";
    public const string Contact = "";
    public const string ContactInfo = "";
    public const string Education = "";
    public const string Library = "";
    public const string Dictionary = "";
    public const string Group = "";
    public const string MapPin = "";
    public const string Calendar = "";
    public const string Clock = "";
    public const string CheckMark = "";
    public const string FavoriteStar = "";
    public const string Edit = "";
    public const string PaymentCard = "";
    public const string Money = "";
    public const string Calculator = "";
    public const string Chart = "";
    public const string Document = "";
    public const string Settings = "";
    public const string Search = "";
    public const string Lock = "";
    public const string SignOut = "";
    public const string Back = "";
    public const string Help = "";
    public const string Keyboard = "";
    public const string Font = "";
    public const string Sun = "";
    public const string Moon = "";
}

/// <summary>
/// Root view model: decides between login, forced password change, lock screen and the application,
/// and owns the sidebar, header, current page, search palette, dialog overlay and toast.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private readonly IThemeService _theme;
    private readonly ISettingsService _settings;
    private readonly TimeProvider _clock;
    private readonly IFileStorage _storage;
    private readonly IUserPreferences _preferences;

    public ShellViewModel(AppSession session, Navigator navigator, DialogHost dialogs, Notifier notifier, IThemeService theme,
        ISettingsService settings, IFileStorage storage, TimeProvider clock, LoginViewModel login, LockViewModel lockScreen, ChangePasswordViewModel changePassword,
        SearchViewModel search, IUserPreferences preferences)
    {
        _preferences = preferences;
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
        Search = search;

        NavGroups =
        [
            new(null, [Item("dashboard", "Tableau de bord", typeof(DashboardViewModel), Glyphs.Home)]),
            new("Scolarité",
            [
                Item("students", "Élèves", typeof(StudentsViewModel), Glyphs.People),
                Item("parents", "Parents", typeof(ParentsViewModel), Glyphs.ContactInfo),
                Item("teachers", "Enseignants", typeof(TeachersViewModel), Glyphs.Education),
                Item("subjects", "Matières", typeof(SubjectsViewModel), Glyphs.Library),
                Item("groups", "Groupes", typeof(GroupsViewModel), Glyphs.Group),
                Item("rooms", "Salles", typeof(RoomsViewModel), Glyphs.MapPin),
            ]),
            new("Planning",
            [
                Item("schedule", "Emploi du temps", typeof(ScheduleViewModel), Glyphs.Calendar),
                Item("sessions", "Séances", typeof(SessionsViewModel), Glyphs.Clock),
                Item("attendance", "Présences", typeof(AttendanceViewModel), Glyphs.CheckMark),
            ]),
            new("Évaluation",
            [
                Item("grades", "Notes", typeof(GradesViewModel), Glyphs.FavoriteStar),
                Item("exams", "Examens", typeof(ExamsViewModel), Glyphs.Edit),
            ]),
            new("Finances",
            [
                Item("payments", "Paiements", typeof(PaymentsViewModel), Glyphs.PaymentCard),
                Item("tpayments", "Paiements enseignants", typeof(TeacherPaymentsViewModel), Glyphs.Money),
                Item("expenses", "Dépenses", typeof(ExpensesViewModel), Glyphs.Calculator),
            ]),
            new("Centre",
            [
                Item("reports", "Rapports", typeof(ReportsViewModel), Glyphs.Chart),
                Item("documents", "Documents", typeof(DocumentsViewModel), Glyphs.Document),
                Item("settings", "Paramètres", typeof(SettingsViewModel), Glyphs.Settings),
            ]),
        ];

        TextScaleOptions =
        [
            new("Normal", 1.0, SelectTextScale),
            new("Grand", 1.15, SelectTextScale),
            new("Très grand", 1.3, SelectTextScale),
        ];
        var savedScale = NearestScale(_preferences.Get(PreferenceKeys.TextScale, 1.0));
        _textScale = savedScale.Value;
        savedScale.IsSelected = true;
        _isHelpOpen = !_preferences.Get(PreferenceKeys.HelpCollapsed, false);

        _session.StateChanged += (_, _) => OnSessionChanged();
        Dialogs.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DialogHost.Current)) OnPropertyChanged(nameof(IsAppInteractive)); };
        Search.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SearchViewModel.IsOpen)) OnPropertyChanged(nameof(IsAppInteractive)); };
        Navigator.Navigated += (_, _) =>
        {
            HighlightNav();
            OnPropertyChanged(nameof(CurrentHelp));
            OnPropertyChanged(nameof(ShowHelpPanel));
            OnPropertyChanged(nameof(PrimaryActionLabel));
        };
        Login.SignedIn += async (_, _) => await EnterAppAsync();
        ChangePassword.Completed += async (_, _) => await EnterAppAsync();
    }

    public Navigator Navigator { get; }
    public DialogHost Dialogs { get; }
    public Notifier Notifier { get; }
    public LoginViewModel Login { get; }
    public LockViewModel Lock { get; }
    public ChangePasswordViewModel ChangePassword { get; }
    /// <summary>Global search palette (Ctrl+K).</summary>
    public SearchViewModel Search { get; }
    public IReadOnlyList<NavGroup> NavGroups { get; }

    public bool ShowLogin => _session.State == SessionState.SignedOut;
    public bool ShowChangePassword => _session.State == SessionState.PasswordChangeRequired;
    public bool ShowApp => _session.State is SessionState.Active or SessionState.Locked;
    public bool ShowLock => _session.State == SessionState.Locked;
    /// <summary>False while locked, while a dialog is open or while the search palette is open, so keyboard focus cannot reach hidden controls.</summary>
    public bool IsAppInteractive => !ShowLock && Dialogs.Current is null && !Search.IsOpen && !IsShortcutsOpen;

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
    public string ThemeGlyph => IsDark ? Glyphs.Sun : Glyphs.Moon;

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

    private NavItem Item(string key, string label, Type page, string glyph)
    {
        var index = PageHelp.QuickPages.ToList().IndexOf(key);
        return new(key, label, page, item => Navigator.NavigateAsync(item.Page), glyph) { Shortcut = index < 0 ? null : $"Ctrl+{index + 1}" };
    }

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
        if (_session.State != SessionState.Active)
        {
            Search.Close();
            IsShortcutsOpen = false; // never leave the F1 panel over the lock or login screen
        }
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
        OnPropertyChanged(nameof(ThemeGlyph));
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

    /// <summary>Enter in the header search box: opens the search palette with the typed text.</summary>
    // "On" prefix: the generated command is still SearchCommand (Search is the palette property).
    [RelayCommand]
    private void OnSearch()
    {
        var q = SearchText.Trim();
        SearchText = "";
        OpenSearch(q);
    }

    /// <summary>Ctrl+K or a click on the header search box. Ignored when the application is not usable (locked, signed out, dialog open).</summary>
    [RelayCommand]
    private void OpenSearch(string? text)
    {
        if (_session.State != SessionState.Active || Dialogs.Current is not null) return;
        IsShortcutsOpen = false; // the palette would otherwise open underneath the F1 panel
        Search.Open(string.IsNullOrWhiteSpace(text) ? null : text.Trim());
    }

    [RelayCommand]
    private void CloseSearch() => Search.Close();

    // ===================== Page help, keyboard shortcuts, text size =====================

    /// <summary>Help of the page on screen ("?" button of the header), or null.</summary>
    public PageHelpEntry? CurrentHelp => PageHelp.For(Navigator.Current?.NavKey);

    /// <summary>Page help panel expanded. Remembered on this computer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHelpPanel))]
    private bool _isHelpOpen;

    public bool ShowHelpPanel => IsHelpOpen && CurrentHelp is not null;

    [RelayCommand]
    private void ToggleHelp()
    {
        IsHelpOpen = !IsHelpOpen;
        _preferences.Set(PreferenceKeys.HelpCollapsed, !IsHelpOpen);
    }

    /// <summary>Keyboard shortcuts and display overlay (F1).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppInteractive))]
    private bool _isShortcutsOpen;

    public IReadOnlyList<ShortcutInfo> Shortcuts => PageHelp.Shortcuts;

    [RelayCommand]
    private void ToggleShortcuts()
    {
        if (IsShortcutsOpen)
        {
            IsShortcutsOpen = false;
            return;
        }
        if (_session.State != SessionState.Active || Dialogs.Current is not null) return;
        Search.Close();
        IsShortcutsOpen = true;
    }

    [RelayCommand]
    private void CloseShortcuts() => IsShortcutsOpen = false;

    /// <summary>Scale factor of the whole window (text and controls): 1.0, 1.15 or 1.3. Remembered on this computer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowClock))]
    private double _textScale = 1.0;

    /// <summary>The header clock is hidden at larger text sizes so the header buttons keep their room.</summary>
    public bool ShowClock => TextScale < 1.1;

    public IReadOnlyList<TextScaleOption> TextScaleOptions { get; }

    private TextScaleOption NearestScale(double value) => TextScaleOptions.MinBy(o => Math.Abs(o.Value - value))!;

    private void SelectTextScale(TextScaleOption option)
    {
        TextScale = option.Value;
        foreach (var o in TextScaleOptions) o.IsSelected = ReferenceEquals(o, option);
        _preferences.Set(PreferenceKeys.TextScale, option.Value);
    }

    /// <summary>Button text of Ctrl+N on the current page, or null when the page has nothing to create.</summary>
    public string? PrimaryActionLabel => Navigator.Current is IHasPrimaryAction { PrimaryCommand: not null } p ? p.PrimaryLabel : null;

    /// <summary>
    /// Keyboard shortcuts act only in the application itself (not locked, no dialog, no search palette).
    /// The F1 overlay is closed first, so a shortcut pressed while reading it still works.
    /// </summary>
    private bool ReadyForShortcut()
    {
        if (_session.State != SessionState.Active || Dialogs.Current is not null || Search.IsOpen) return false;
        IsShortcutsOpen = false;
        return true;
    }

    /// <summary>F5: reloads the current page.</summary>
    [RelayCommand]
    private async Task RefreshPage()
    {
        if (!ReadyForShortcut() || Navigator.Current is not { } page) return;
        await page.RefreshAsync();
    }

    /// <summary>Alt+← and the header "← Retour" button.</summary>
    [RelayCommand]
    private async Task GoBack()
    {
        if (!ReadyForShortcut() || !Navigator.CanGoBack) return;
        await Navigator.BackAsync();
    }

    /// <summary>Ctrl+N: the main "add" action of the current page, when it has one.</summary>
    [RelayCommand]
    private async Task NewItem()
    {
        if (!ReadyForShortcut()) return;
        if (Navigator.Current is IHasPrimaryAction { PrimaryCommand: { } command } && command.CanExecute(null))
            await command.ExecuteAsync(null);
        else
            Notifier.Info("Ctrl+N : rien à créer sur cette page.");
    }

    /// <summary>Ctrl+P: prints the current page, when it can be printed directly.</summary>
    [RelayCommand]
    private async Task PrintPage()
    {
        if (!ReadyForShortcut()) return;
        if (Navigator.Current is IHasPrintAction { PrintCommand: { } command } && command.CanExecute(null))
            await command.ExecuteAsync(null);
        else
            Notifier.Info("Ctrl+P : cette page ne s'imprime pas directement. Utilisez ses boutons d'impression.");
    }

    /// <summary>Ctrl+1 … Ctrl+9 (parameter: the digit) or a page key ("students").</summary>
    [RelayCommand]
    private async Task GoTo(string? target)
    {
        if (!ReadyForShortcut() || string.IsNullOrEmpty(target)) return;
        var key = int.TryParse(target, out var n) && n >= 1 && n <= PageHelp.QuickPages.Count ? PageHelp.QuickPages[n - 1] : target;
        if (NavGroups.SelectMany(g => g.Items).FirstOrDefault(i => i.Key == key) is { } item)
            await item.GoCommand.ExecuteAsync(null);
    }
}
