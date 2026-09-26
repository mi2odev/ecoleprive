using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CentreSoutien.Presentation.ViewModels.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Presentation.ViewModels.Pages;

/// <summary>
/// All settings of the center and the application. The form edits a copy of <see cref="CenterSettings"/>
/// that is saved as a whole by "Enregistrer".
/// </summary>
public sealed partial class SettingsViewModel(
    ISettingsService settings, IBackupService backup, IExportService export, IDemoDataService demo, ISystemInfo system,
    IFileStorage storage, IFilePicker files, IShell platform, IThemeService theme, ShellViewModel shell, AppSession session,
    INavigator nav, DialogHost dialogs, INotifier notifier, TimeProvider clock, IServiceProvider services) : PageViewModel
{
    private bool _loading;

    public override string NavKey => "settings";
    public override string Title => "Paramètres";

    public IReadOnlyList<Option<string>> Sections { get; } =
    [
        new("centre", "Centre"),
        new("tarifs", "Tarifs, paiements et remises"),
        new("remun", "Rémunération enseignants"),
        new("eval", "Présences et notes"),
        new("recus", "Reçus"),
        new("messages", "Messages aux parents"),
        new("backup", "Sauvegarde et export"),
        new("app", "Langue et thème"),
        new("sec", "Sécurité"),
    ];

    public IReadOnlyList<Option<CompensationType>> CompensationTypes => Options.CompensationTypes;
    [ObservableProperty] private IReadOnlyList<Option<int>> _lockDelays = [];
    [ObservableProperty] private IReadOnlyList<Option<int>> _sessionTimeouts = [];
    public IReadOnlyList<Option<AppTheme>> Themes { get; } = [new(AppTheme.Light, "Clair"), new(AppTheme.Dark, "Sombre")];

    [ObservableProperty] private Option<string>? _selectedSection;

    /// <summary>Editable copy of the settings.</summary>
    [ObservableProperty] private CenterSettings _form = new();

    [ObservableProperty] private string? _logoPath;
    [ObservableProperty] private Option<CompensationType>? _selectedCompensation;
    [ObservableProperty] private string _autoBackupTime = "22:00";
    [ObservableProperty] private string _lastBackupLabel = "Jamais";
    [ObservableProperty] private string _defaultBackupFolder = "";
    [ObservableProperty] private string? _backupFolder;
    [ObservableProperty] private bool _canLoadDemo;
    [ObservableProperty] private Option<int>? _selectedLockDelay;
    [ObservableProperty] private Option<int>? _selectedSessionTimeout;
    [ObservableProperty] private Option<AppTheme>? _selectedTheme;

    public bool HasCustomLogo => LogoPath is not null;
    partial void OnLogoPathChanged(string? value) => OnPropertyChanged(nameof(HasCustomLogo));

    public string Section => SelectedSection?.Value ?? "centre";
    public bool IsCentre => Section == "centre";
    public bool IsTarifs => Section == "tarifs";
    public bool IsRemun => Section == "remun";
    public bool IsEval => Section == "eval";
    public bool IsRecus => Section == "recus";
    public bool IsMessages => Section == "messages";
    public bool IsBackup => Section == "backup";
    public bool IsApp => Section == "app";
    public bool IsSecurity => Section == "sec";

    partial void OnSelectedSectionChanged(Option<string>? value)
    {
        foreach (var p in new[] { nameof(Section), nameof(IsCentre), nameof(IsTarifs), nameof(IsRemun), nameof(IsEval), nameof(IsRecus), nameof(IsMessages), nameof(IsBackup), nameof(IsApp), nameof(IsSecurity) })
            OnPropertyChanged(p);
    }

    // ----- Messages aux parents -----

    /// <summary>"{parent} nom du parent · {eleve} …" shown under the templates.</summary>
    public string PlaceholderHelp => MessageTemplates.PlaceholderHelp;

    // Bound to the template boxes (the Form properties do not notify, the previews must follow the typing).
    [ObservableProperty] private string _paymentTemplate = "";
    [ObservableProperty] private string _absenceTemplate = "";
    [ObservableProperty] private string _paymentPreview = "";
    [ObservableProperty] private string _absencePreview = "";

    partial void OnPaymentTemplateChanged(string value)
    {
        Form.PaymentReminderTemplate = value;
        UpdatePreviews();
    }

    partial void OnAbsenceTemplateChanged(string value)
    {
        Form.AbsenceMessageTemplate = value;
        UpdatePreviews();
    }

    /// <summary>Example messages for a fictitious student, so the owner sees the result while editing.</summary>
    private void UpdatePreviews()
    {
        var month = Period.Of(clock.GetLocalNow().DateTime);
        PaymentPreview = MessageTemplates.PaymentReminder(Form, "M. Benali", "Yacine Benali", month, 4500, 2000);
        AbsencePreview = MessageTemplates.AbsenceNotice(Form, "M. Benali", "Yacine Benali", "Mathématiques", clock.GetLocalNow().DateTime.Date);
    }

    [RelayCommand]
    private void ResetPaymentTemplate() => PaymentTemplate = CenterSettings.DefaultPaymentReminderTemplate;

    [RelayCommand]
    private void ResetAbsenceTemplate() => AbsenceTemplate = CenterSettings.DefaultAbsenceMessageTemplate;

    public string DatabaseLabel => system.DatabaseEncrypted ? "Chiffrée (SQLCipher, AES-256)" : "Non chiffrée";
    public string KeyProtection => system.KeyProtection;
    public string DataFolder => system.DataFolder;

    public override async Task LoadAsync(object? parameter)
    {
        SelectedSection ??= Sections.FirstOrDefault(s => s.Value == parameter as string) ?? Sections[0];
        if (parameter is string key && Sections.FirstOrDefault(s => s.Value == key) is { } section) SelectedSection = section;
        await RunAsync(async () =>
        {
            _loading = true;
            try
            {
                var s = await settings.GetAsync();
                Form = s;
                LogoPath = s.LogoFile is null ? null : storage.GetPath(s.LogoFile, StorageAreas.Images);
                SelectedCompensation = CompensationTypes.First(c => c.Value == s.DefaultCompensationType);
                AutoBackupTime = Parse.Time(s.AutoBackupTime);
                DefaultBackupFolder = backup.DefaultFolder;
                UpdateLastBackup(s);
                BackupFolder = s.BackupFolder;
                LockDelays = Minutes(Options.LockDelays, s.AutoLockMinutes);
                SessionTimeouts = Minutes([30, 60, 120, 240], s.SessionTimeoutMinutes);
                SelectedLockDelay = LockDelays.First(o => o.Value == s.AutoLockMinutes);
                SelectedSessionTimeout = SessionTimeouts.First(o => o.Value == s.SessionTimeoutMinutes);
                SelectedTheme = Themes.First(t => t.Value == s.Theme);
                PaymentTemplate = s.PaymentReminderTemplate;
                AbsenceTemplate = s.AbsenceMessageTemplate;
                UpdatePreviews();
                CanLoadDemo = await demo.IsDatabaseEmptyAsync();
            }
            finally
            {
                _loading = false;
            }
        }, notifier);
    }

    /// <summary>Chips for a delay in minutes; a stored value outside the proposed ones is kept as an extra chip.</summary>
    private static IReadOnlyList<Option<int>> Minutes(IEnumerable<int> values, int current) =>
        values.Append(current).Distinct().Order().Select(n => new Option<int>(n, n switch
        {
            0 => "Jamais",
            >= 60 when n % 60 == 0 => $"{n / 60} h",
            _ => $"{n} min",
        })).ToList();

    private void UpdateLastBackup(CenterSettings s) =>
        LastBackupLabel = s.LastBackupAt is { } at
            ? $"{at:dd/MM/yyyy HH:mm}{(s.LastBackupSize is { } size ? " · " + FormatSize(size) : "")}"
            : "Jamais";

    private static string FormatSize(long bytes) => bytes < 1024 * 1024 ? $"{Math.Max(1, bytes / 1024)} Ko" : $"{bytes / 1024.0 / 1024.0:0.0} Mo";

    partial void OnSelectedCompensationChanged(Option<CompensationType>? value)
    {
        if (value is not null) Form.DefaultCompensationType = value.Value;
    }

    partial void OnSelectedLockDelayChanged(Option<int>? value)
    {
        if (value is not null) Form.AutoLockMinutes = value.Value;
    }

    partial void OnSelectedSessionTimeoutChanged(Option<int>? value)
    {
        if (value is not null) Form.SessionTimeoutMinutes = value.Value;
    }

    async partial void OnSelectedThemeChanged(Option<AppTheme>? value)
    {
        if (value is null) return;
        Form.Theme = value.Value;
        if (_loading || theme.Current == value.Value) return;
        theme.Apply(value.Value);
        shell.Refresh();
        // The theme is applied immediately; persist it like the header toggle does.
        await RunAsync(async () =>
        {
            var s = await settings.GetAsync();
            s.Theme = value.Value;
            await settings.SaveAsync(s);
        }, notifier);
    }

    [RelayCommand]
    private async Task Save()
    {
        if (IsBusy) return;
        await RunAsync(async () =>
        {
            var time = Parse.Time(AutoBackupTime) ?? throw new BusinessException("Heure de sauvegarde invalide (ex. 22:00).");
            var f = Form;
            f.AutoBackupTime = time;
            f.BackupFolder = string.IsNullOrWhiteSpace(BackupFolder) ? null : BackupFolder.Trim();
            f.Currency = string.IsNullOrWhiteSpace(f.Currency) ? "DZD" : f.Currency.Trim().ToUpperInvariant();
            if (f.NextReceiptNumber < 1) throw new BusinessException("Le prochain numéro de reçu doit être positif.");
            if (f.BackupRetentionCount < 1) throw new BusinessException("Conservez au moins une sauvegarde.");
            f.PaymentReminderTemplate = string.IsNullOrWhiteSpace(PaymentTemplate) ? CenterSettings.DefaultPaymentReminderTemplate : PaymentTemplate.Trim();
            f.AbsenceMessageTemplate = string.IsNullOrWhiteSpace(AbsenceTemplate) ? CenterSettings.DefaultAbsenceMessageTemplate : AbsenceTemplate.Trim();
            var cc = new string((f.PhoneCountryCode ?? "").Where(char.IsAsciiDigit).ToArray());
            if (cc.Length is < 1 or > 4) throw new BusinessException("Indicatif téléphonique invalide (ex. 213 pour l'Algérie).");
            f.PhoneCountryCode = cc;

            // Keep values changed elsewhere since the page was opened (backup stamps, receipt numbers issued meanwhile).
            var current = await settings.GetAsync();
            f.LastBackupAt = current.LastBackupAt;
            f.LastBackupSize = current.LastBackupSize;
            if (current.NextReceiptNumber > f.NextReceiptNumber && current.ReceiptPrefix == f.ReceiptPrefix) f.NextReceiptNumber = current.NextReceiptNumber;

            var saved = await settings.SaveAsync(f);
            shell.ApplySettings(saved);
            if (theme.Current != saved.Theme) theme.Apply(saved.Theme);
            shell.Refresh();
            notifier.Info("Paramètres enregistrés");
        }, notifier);
    }

    // ----- Centre -----

    [RelayCommand]
    private void ReplaceLogo()
    {
        var path = files.OpenFile("Choisir le logo du centre", "Images|*.png;*.jpg;*.jpeg;*.bmp");
        if (path is null) return;
        try
        {
            Form.LogoFile = storage.Import(path, StorageAreas.Images);
            LogoPath = storage.GetPath(Form.LogoFile, StorageAreas.Images);
            notifier.Info("Logo chargé · enregistrez pour l'appliquer");
        }
        catch (BusinessException ex)
        {
            notifier.Error(ex.Message);
        }
    }

    [RelayCommand]
    private void DefaultLogo()
    {
        Form.LogoFile = null;
        LogoPath = null;
    }

    // ----- Links to other screens -----

    [RelayCommand] private Task GoCourses() => nav.NavigateAsync<GroupsViewModel>();
    [RelayCommand] private Task GoDiscounts() => nav.NavigateAsync<PaymentsViewModel>();
    [RelayCommand] private Task GoAccount() => nav.NavigateAsync<AccountViewModel>();

    // ----- Backup & export -----

    [RelayCommand]
    private void BrowseBackupFolder()
    {
        var folder = files.PickFolder("Dossier des sauvegardes");
        if (folder is null) return;
        BackupFolder = folder;
    }

    /// <summary>Path of the last backup made from this page (tests, toast).</summary>
    public string? LastBackupFile { get; private set; }

    [RelayCommand]
    private async Task BackupNow()
    {
        await RunAsync(async () =>
        {
            var file = await backup.BackupAsync(string.IsNullOrWhiteSpace(BackupFolder) ? null : BackupFolder.Trim());
            LastBackupFile = file;
            var s = await settings.GetAsync();
            Form.LastBackupAt = s.LastBackupAt;
            Form.LastBackupSize = s.LastBackupSize;
            UpdateLastBackup(s);
            notifier.Info("Sauvegarde effectuée : " + file);
            platform.Reveal(file);
        }, notifier);
    }

    [RelayCommand]
    private async Task Restore()
    {
        var dialog = services.GetRequiredService<RestoreBackupDialogViewModel>();
        dialog.PickFile();
        if (!dialog.HasFile) return;
        if (!await dialogs.ShowAsync(dialog)) return;
        notifier.Info("Restauration terminée. Reconnectez-vous.");
        session.SignOut();
    }

    [RelayCommand]
    private async Task ExportData()
    {
        var path = files.SaveFile("Exporter les données", $"centre-export-{clock.GetLocalNow():yyyy-MM-dd}.xlsx", "Classeur Excel|*.xlsx");
        if (path is null) return;
        await RunAsync(async () =>
        {
            await export.ExportAllAsync(path);
            notifier.Info("Export Excel généré");
            platform.Reveal(path);
        }, notifier);
    }

    [RelayCommand]
    private async Task LoadDemo()
    {
        if (!await dialogs.ConfirmAsync("Données de démonstration",
                "Charger des élèves, enseignants, groupes et paiements fictifs pour essayer l'application ? Vous pourrez les supprimer ensuite.",
                "Charger", danger: false))
            return;
        if (await RunAsync(() => demo.SeedAsync(), notifier))
        {
            notifier.Info("Données de démonstration chargées");
            await RefreshAsync();
        }
    }

    // ----- Security -----

    [RelayCommand]
    private void OpenDataFolder() => platform.Reveal(system.DataFolder);
}
