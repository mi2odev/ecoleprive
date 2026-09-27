using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Presentation.ViewModels.Pages;

/// <summary>
/// One line of the payments table: a student in one group (each group is paid on its own), or a student without group.
/// </summary>
/// <param name="Waiting">"12 j" since the oldest unpaid pack of the group started, "—" when nothing is due.</param>
public sealed record PayLine(PaymentRow Item, GroupAccount? Account, string Waiting, IRelayCommand Open, IRelayCommand Collect)
{
    public int StudentId => Item.StudentId;
    public int? GroupId => Account?.GroupId;
    public string Name => Item.FullName;
    /// <summary>" · −10 %" when a discount applies (muted after the name).</summary>
    public string Discount => Item.Discount is null ? "" : " · " + DiscountValue(Item.Discount);
    public string Due => Money.Format(Account?.Due ?? 0);
    public string Paid => Money.Format(Account?.Paid ?? 0);
    public string PackPrice => Account is { PackPrice: > 0 } a ? Money.Format(a.PackPrice) : "—";
    /// <summary>"Mathématiques · 3AS A · séance 3/4".</summary>
    public string Progress => Account is null ? "Aucun groupe"
        : Account.Status is { } st ? $"{Account.Group} · séance {Math.Min(st.Done + 1, st.Size)}/{st.Size}" : $"{Account.Group} · a quitté le groupe";
    public string Rest => Account is null ? "—"
        : Account.Balance <= 0 && Account.Credit > 0 ? $"+{Money.Format(Account.Credit)} d'avance" : Money.Format(Account.Balance);
    public decimal BalanceValue => Account?.Balance ?? 0;
    public Badge State => Badge.For(Account?.State ?? Item.State);
    public bool CanCollect => Account is { Balance: > 0 };

    // Discount.ToString() is "Name (−10 %)": keep the value only.
    private static string DiscountValue(string label)
    {
        var i = label.LastIndexOf('(');
        return i >= 0 && label.EndsWith(')') ? label[(i + 1)..^1] : label;
    }
}

/// <summary>Compact receipt entry of the "Derniers reçus" column.</summary>
public sealed record PayRecentReceipt(string Name, string Details, string Amount, IRelayCommand Print);

/// <summary>Receipt line of the "Reçus" tab. <see cref="State"/> is "Valide" or "Annulé" (reason in <see cref="StateDetails"/>).</summary>
public sealed record PayReceiptLine(int Id, string Number, string Date, string Student, string Kind, string Month, string Method, string Amount,
    IRelayCommand Print, IRelayCommand Delete, bool IsCancelled = false, string? StateDetails = null)
{
    public Badge State => IsCancelled ? new Badge("Annulé", BadgeKind.Bad) : new Badge("Valide", BadgeKind.Ok);
    public bool CanCancel => !IsCancelled;
}

/// <summary>Discount line of the "Remises" tab.</summary>
public sealed record DiscountLine(int Id, string Name, string Type, string Value, Badge Active, string Students, string? Notes, IRelayCommand Edit, IRelayCommand Delete);

/// <summary>Student with an unpaid balance in the "Relances" tab: parent contact, delay and the reminder texts.</summary>
public sealed partial class ReminderLine(
    PaymentRow item, string? parentName, int daysLate, Badge delay, string message, string shortMessage, string? whatsAppUrl,
    IRelayCommand open, IRelayCommand copy, IRelayCommand whatsApp, IRelayCommand sms) : ObservableObject
{
    public PaymentRow Item => item;
    public int StudentId => item.StudentId;
    public string Name => item.FullName;
    public string Details => $"{item.Matricule} · {item.Level}";
    public string Parent => parentName ?? "Aucun parent";
    public string? ParentName => parentName;
    public string Phone => string.IsNullOrWhiteSpace(item.ParentPhone) ? "Pas de téléphone" : item.ParentPhone;
    public string Rest => Money.Format(item.Balance);
    public string Due => Money.Format(item.Due);
    /// <summary>Days since the payment was due (pack start + payment delay; negative before it).</summary>
    public int DaysLate => daysLate;
    public string DelayLabel => daysLate > 0 ? $"{daysLate} j de retard" : daysLate == 0 ? "Échéance aujourd'hui" : $"Échéance dans {-daysLate} j";
    public Badge Delay => delay;
    public string Message => message;
    public string ShortMessage => shortMessage;
    public string? WhatsAppUrl => whatsAppUrl;
    public bool CanWhatsApp => whatsAppUrl is not null;
    public IRelayCommand Open => open;
    public IRelayCommand Copy => copy;
    public IRelayCommand WhatsApp => whatsApp;
    public IRelayCommand Sms => sms;

    [ObservableProperty] private bool _isSelected = true;
    public Action? SelectionChanged { get; set; }
    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke();
}

public sealed partial class PaymentsViewModel(
    IPaymentService payments, IStudentService students, ICrudService<Discount> discounts, ISettingsService settings, IFileStorage storage,
    IPrintService printer, IExportService export, IFilePicker files, IShell shell, IClipboard clipboard, ILauncher launcher,
    INavigator nav, DialogHost dialogs, INotifier notifier, TimeProvider clock, IServiceProvider services) : PageViewModel
{
    private List<PaymentRow> _all = [];
    private CenterSettings _settings = new();
    private bool _syncingSelection;

    public override string NavKey => "payments";
    public override string Title => "Paiements des élèves";

    [ObservableProperty] private IReadOnlyList<Option<DateTime>> _months = [];
    [ObservableProperty] private Option<DateTime>? _selectedMonth;
    [ObservableProperty] private string _monthLabel = "";
    [ObservableProperty] private string _tab = "monthly";
    private List<StudentPayment> _monthReceipts = [];

    [ObservableProperty] private IReadOnlyList<Kpi> _kpis = [];
    [ObservableProperty] private IReadOnlyList<string> _filters = ["Tous", "Payé", "Partiel", "Impayé"];
    [ObservableProperty] private string _selectedFilter = "Tous";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private IReadOnlyList<PayLine> _rows = [];
    [ObservableProperty] private string _countLabel = "";
    [ObservableProperty] private IReadOnlyList<PayRecentReceipt> _latestReceipts = [];
    [ObservableProperty] private IReadOnlyList<PayReceiptLine> _receipts = [];
    [ObservableProperty] private string _receiptsTotal = "";
    [ObservableProperty] private IReadOnlyList<DiscountLine> _discountLines = [];
    [ObservableProperty] private IReadOnlyList<ReminderLine> _reminders = [];
    [ObservableProperty] private string _remindersSummary = "";
    [ObservableProperty] private string _selectionLabel = "";
    [ObservableProperty] private string _dueDateLabel = "";
    /// <summary>"Tout sélectionner" checkbox; null when only some rows are selected.</summary>
    [ObservableProperty] private bool? _allRemindersSelected = true;
    /// <summary>No active student ("Séances" tab empty state).</summary>
    [ObservableProperty] private bool _hasNoData;
    /// <summary>Students are billed but the search / status filter hides all of them.</summary>
    [ObservableProperty] private bool _hasNoResults;
    [ObservableProperty] private bool _hasNoReceipts;
    [ObservableProperty] private bool _hasNoDiscounts;
    [ObservableProperty] private bool _hasNoReminders;

    public bool IsMonthly => Tab == "monthly";
    public bool IsReceipts => Tab == "receipts";
    public bool IsDiscounts => Tab == "discounts";
    public bool IsReminders => Tab == "reminders";

    partial void OnTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsMonthly));
        OnPropertyChanged(nameof(IsReceipts));
        OnPropertyChanged(nameof(IsDiscounts));
        OnPropertyChanged(nameof(IsReminders));
    }

    [RelayCommand] private void ShowTab(string tab) => Tab = tab;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedFilterChanged(string value) => ApplyFilter();

    async partial void OnSelectedMonthChanged(Option<DateTime>? oldValue, Option<DateTime>? newValue)
    {
        if (oldValue is not null && newValue is not null) await ReloadAsync();
    }

    private DateTime Month => SelectedMonth?.Value ?? Period.Of(clock.GetLocalNow().DateTime);

    public override async Task LoadAsync(object? parameter)
    {
        if (Months.Count == 0)
        {
            var now = clock.GetLocalNow().DateTime;
            Months = Options.Months(now);
            var start = parameter switch { DateTime d => d, Target { Month: { } m } => m, _ => now };
            SelectedMonth = Months.FirstOrDefault(m => m.Value == Period.Of(start)) ?? Months.First(m => m.Value == Period.Of(now));
        }
        if (parameter is Target { Tab: { } tab }) Tab = tab;
        await ReloadAsync();
    }

    private Task ReloadAsync() => RunAsync(async () =>
    {
        var month = Month;
        MonthLabel = Labels.Month(month);
        _settings = await settings.GetAsync();
        _all = await payments.OverviewAsync();
        var studentList = await students.ListAsync(clock.GetLocalNow().DateTime);
        await LoadReceiptsAsync();
        var rest = _all.Sum(r => r.Balance);
        var owing = _all.Count(r => r.Balance > 0);
        var late = _all.Count(r => DaysLate(r) > 0);
        var advance = _all.Sum(r => r.Credit);
        Kpis =
        [
            new("Reste à percevoir", Money.Format(rest), $"{owing} élève{S(owing)} concerné{S(owing)}"),
            new("En retard", late.ToString(), $"délai de paiement de {_settings.PaymentDueDay} j dépassé"),
            new("Encaissé", Money.Format(_monthReceipts.Sum(p => p.Amount)), MonthLabel),
            new("Payé d'avance", Money.Format(advance), $"{_all.Count(r => r.Credit > 0)} élève{S(_all.Count(r => r.Credit > 0))}"),
        ];
        ApplyFilter();
        LoadReminders(studentList);
        await LoadDiscountsAsync(studentList);
    }, notifier);

    private static string S(int n) => n > 1 ? "s" : "";

    /// <summary>Days since the oldest unpaid pack became due (its start + the payment delay); null when nothing is due.</summary>
    private int? DaysLate(PaymentRow r) => r.Balance > 0 && r.DueSince is { } since
        ? (clock.GetLocalNow().Date - since.Date).Days - Math.Max(0, _settings.PaymentDueDay)
        : null;

    private string Waiting(GroupAccount? a)
    {
        if (a is null || a.Balance <= 0 || a.DueSince is not { } since) return "—";
        var days = (clock.GetLocalNow().Date - since.Date).Days;
        return days <= 0 ? "aujourd'hui" : $"{days} j";
    }

    private void ApplyFilter()
    {
        var q = SearchText.Trim().ToLowerInvariant();
        var list = _all
            .Where(r => SelectedFilter == "Tous" || r.GroupAccounts.Any(a => Labels.Of(a.State) == SelectedFilter) || (r.GroupAccounts.Count == 0 && Labels.Of(r.State) == SelectedFilter))
            .Where(r => q.Length == 0 || $"{r.FullName} {r.Matricule} {r.ParentPhone}".ToLowerInvariant().Contains(q))
            .ToList();
        Rows = list.SelectMany(r => (r.GroupAccounts.Count == 0 ? [null] : r.GroupAccounts.Cast<GroupAccount?>()).Select(a => new PayLine(r, a, Waiting(a),
                new AsyncRelayCommand(() => nav.NavigateAsync<StudentDetailViewModel>(r.StudentId)),
                new AsyncRelayCommand(() => CollectAsync(r.StudentId, a?.GroupId)))))
            .Where(l => SelectedFilter == "Tous" || l.State.Text == SelectedFilter)
            .ToList();
        CountLabel = $"{list.Count} élève{(list.Count > 1 ? "s" : "")} sur {_all.Count} · {Rows.Count} ligne{(Rows.Count > 1 ? "s" : "")} (une par groupe)";
        HasNoData = _all.Count == 0;
        HasNoResults = _all.Count > 0 && list.Count == 0;
    }

    /// <summary>Clears the search and the payment status filter.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = "";
        SelectedFilter = "Tous";
    }

    /// <summary>Empty-state shortcut: the students page (to add or import students).</summary>
    [RelayCommand]
    private Task OpenStudents() => nav.NavigateAsync<StudentsViewModel>();

    private async Task LoadReceiptsAsync()
    {
        var month = Month;
        var list = await payments.ReceiptsAsync(month, Period.End(month));
        _monthReceipts = list.Where(p => !p.IsCancelled).ToList();
        LatestReceipts = _monthReceipts.Take(12).Select(p => new PayRecentReceipt(
            p.Student?.FullName ?? "—", $"{p.ReceiptNumber} · {p.Date:dd/MM} · {p.Group?.FullName ?? Labels.Of(p.Kind)}", Money.Format(p.Amount),
            new AsyncRelayCommand(() => PrintReceiptAsync(p.Id)))).ToList();
        Receipts = list.Select(p => new PayReceiptLine(p.Id, p.ReceiptNumber, p.Date.ToString("dd/MM/yyyy"), p.Student?.FullName ?? "—",
            p.Group is { } g ? g.FullName : Labels.Of(p.Kind), Labels.Month(p.Period), Labels.Of(p.Method), Money.Format(p.Amount),
            new AsyncRelayCommand(() => PrintReceiptAsync(p.Id)),
            new AsyncRelayCommand(() => CancelReceiptAsync(p)),
            p.IsCancelled, p.IsCancelled ? $"Annulé le {p.CancelledAt:dd/MM/yyyy HH:mm} · {p.CancelReason}" : null)).ToList();
        var valid = _monthReceipts;
        var cancelled = list.Count - valid.Count;
        ReceiptsTotal = $"{valid.Count} reçu{(valid.Count > 1 ? "s" : "")} · {Money.Format(valid.Sum(p => p.Amount))}"
                        + (cancelled > 0 ? $" · {cancelled} annulé{(cancelled > 1 ? "s" : "")}" : "");
        HasNoReceipts = list.Count == 0;
    }

    private async Task LoadDiscountsAsync(List<StudentListItem> studentList)
    {
        var all = await discounts.ListAsync();
        DiscountLines = all.Select(d =>
        {
            var n = studentList.Count(s => s.IsActive && s.DiscountLabel == d.ToString());
            return new DiscountLine(d.Id, d.Name, Labels.Of(d.Type), d.ValueLabel, Badge.Active(d.IsActive),
                n == 0 ? "Aucun élève" : $"{n} élève{(n > 1 ? "s" : "")}", d.Notes,
                new AsyncRelayCommand(() => EditDiscountAsync(d)),
                new AsyncRelayCommand(() => DeleteDiscountAsync(d, n)));
        }).ToList();
        HasNoDiscounts = DiscountLines.Count == 0;
    }

    [RelayCommand]
    private async Task Collect()
    {
        var dialog = services.GetRequiredService<CollectPaymentDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(null), notifier)) return;
        if (await dialogs.ShowAsync(dialog)) await ReloadAsync();
    }

    private async Task CollectAsync(int studentId, int? groupId)
    {
        var dialog = services.GetRequiredService<CollectPaymentDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(studentId, groupId), notifier)) return;
        if (await dialogs.ShowAsync(dialog)) await ReloadAsync();
    }

    private async Task PrintReceiptAsync(int paymentId)
    {
        await RunAsync(async () =>
        {
            var p = await payments.GetReceiptAsync(paymentId) ?? throw new BusinessException("Reçu introuvable.");
            var cfg = await settings.GetAsync();
            printer.PrintReceipt(p, cfg, cfg.LogoFile is null ? null : storage.GetPath(cfg.LogoFile, StorageAreas.Images), duplicate: true);
        }, notifier);
    }

    /// <summary>End-of-day cash journal (today by default).</summary>
    [RelayCommand]
    private async Task CashJournal()
    {
        var dialog = services.GetRequiredService<CashJournalDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(), notifier)) return;
        await dialogs.ShowAsync(dialog);
    }

    private async Task CancelReceiptAsync(StudentPayment p)
    {
        var dialog = services.GetRequiredService<CancelReceiptDialogViewModel>();
        dialog.Initialize(p);
        if (await dialogs.ShowAsync(dialog)) await ReloadAsync();
    }

    [RelayCommand]
    private Task AddDiscount() => EditDiscountAsync(null);

    private async Task EditDiscountAsync(Discount? d)
    {
        var dialog = services.GetRequiredService<DiscountEditorDialogViewModel>();
        dialog.Initialize(d);
        if (!await dialogs.ShowAsync(dialog)) return;
        notifier.Info(d is null ? "Remise créée" : "Remise enregistrée");
        await ReloadAsync();
    }

    private async Task DeleteDiscountAsync(Discount d, int users)
    {
        var message = users > 0
            ? $"Supprimer la remise « {d.Name} » ? Elle est appliquée à {users} élève{(users > 1 ? "s" : "")}, qui n'en bénéficieront plus."
            : $"Supprimer la remise « {d.Name} » ?";
        if (!await dialogs.ConfirmAsync("Supprimer la remise", message)) return;
        if (await RunAsync(() => discounts.DeleteAsync(d.Id), notifier))
        {
            notifier.Info("Remise supprimée");
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private async Task Export()
    {
        var today = clock.GetLocalNow().DateTime;
        var path = files.SaveFile("Exporter les paiements", $"paiements-{today:yyyy-MM-dd}.xlsx", "Classeur Excel|*.xlsx");
        if (path is null) return;
        await RunAsync(async () =>
        {
            await export.ExportTableAsync(path, "Paiements " + today.ToString("yyyy-MM-dd"),
                ["Matricule", "Élève", "Niveau", "Groupe", "Séance", "Remise", "Prix des séances", "Facturé", "Payé", "Reste", "Payé d'avance", "Statut", "Téléphone parent"],
                _all.SelectMany(r => r.GroupAccounts.Select(a => (IReadOnlyList<object?>)[r.Matricule, r.FullName, r.Level, a.Group,
                    a.Status is { } st ? $"{Math.Min(st.Done + 1, st.Size)}/{st.Size}" : "a quitté", r.Discount, a.PackPrice, a.Due, a.Paid, a.Balance, a.Credit,
                    Labels.Of(a.State), r.ParentPhone])));
            notifier.Info("Export Excel généré");
            shell.Reveal(path);
        }, notifier);
    }

    // ----- Relances -----

    /// <summary>What is owed, group by group: "Mathématiques · 3AS A : 4 500 DZD, Physique · 3AS A : 2 000 DZD".</summary>
    public static string Owed(PaymentRow r) =>
        string.Join(", ", r.GroupAccounts.Where(a => a.Balance > 0).Select(a => $"{a.Group} : {Money.Format(a.Balance)}"));

    /// <summary>Date a student's payment was due: start of the oldest unpaid pack + the payment delay.</summary>
    private DateTime DueDate(PaymentRow r) => (r.DueSince ?? clock.GetLocalNow().Date).Date.AddDays(Math.Max(0, _settings.PaymentDueDay));

    private void LoadReminders(List<StudentListItem> studentList)
    {
        var parents = studentList.ToDictionary(s => s.Id, s => s.ParentName);
        DueDateLabel = $"Délai de paiement : {_settings.PaymentDueDay} j après le début des séances";

        var lines = _all.Where(r => r.Balance > 0).OrderByDescending(r => DaysLate(r)).ThenBy(r => r.FullName).Select(r =>
        {
            var daysLate = DaysLate(r) ?? 0;
            var delay = daysLate >= Math.Max(1, _settings.ReminderAfterDays) ? new Badge("À relancer", BadgeKind.Bad)
                : daysLate > 0 ? new Badge("En retard", BadgeKind.Warn)
                : new Badge("À échoir", BadgeKind.Neutral);
            var parent = parents.GetValueOrDefault(r.StudentId);
            var message = MessageTemplates.PaymentReminder(_settings, parent, r.FullName, r.DueSince ?? clock.GetLocalNow().Date, Owed(r), r.PackPrice, r.Balance);
            var sms = MessageTemplates.ShortPaymentReminder(_settings, r.FullName, r.Balance);
            var url = MessageTemplates.WhatsAppUrl(r.ParentPhone, _settings.PhoneCountryCode, message);
            var line = new ReminderLine(r, parent, daysLate, delay, message, sms, url,
                new AsyncRelayCommand(() => nav.NavigateAsync<StudentDetailViewModel>(r.StudentId)),
                new RelayCommand(() => CopyText(message, "Message copié · " + r.FullName)),
                new RelayCommand(() => OpenWhatsApp(url)),
                new RelayCommand(() => CopyText(sms, "Texte SMS copié · " + r.FullName)));
            line.SelectionChanged = UpdateSelection;
            return line;
        }).ToList();
        Reminders = lines;
        HasNoReminders = lines.Count == 0;
        var total = lines.Sum(l => l.Item.Balance);
        RemindersSummary = lines.Count == 0
            ? "Aucune séance impayée"
            : $"{lines.Count} élève{(lines.Count > 1 ? "s" : "")} · {Money.Format(total)} à percevoir";
        UpdateSelection();
    }

    private List<ReminderLine> SelectedReminders => Reminders.Where(r => r.IsSelected).ToList();

    private void UpdateSelection()
    {
        if (_syncingSelection) return;
        var n = Reminders.Count(r => r.IsSelected);
        SelectionLabel = n == 0 ? "Aucun élève sélectionné" : $"{n} sélectionné{(n > 1 ? "s" : "")}";
        _syncingSelection = true;
        AllRemindersSelected = Reminders.Count > 0 && n == Reminders.Count ? true : n == 0 ? false : null;
        _syncingSelection = false;
    }

    partial void OnAllRemindersSelectedChanging(bool? oldValue, bool? newValue) => _allWasPartial = oldValue is null;

    private bool _allWasPartial;

    partial void OnAllRemindersSelectedChanged(bool? value)
    {
        if (_syncingSelection || value is null) return;
        // Clicking the header box while only some rows are selected selects them all (not none).
        if (value == false && _allWasPartial)
        {
            AllRemindersSelected = true;
            return;
        }
        _syncingSelection = true;
        foreach (var r in Reminders) r.IsSelected = value.Value;
        _syncingSelection = false;
        UpdateSelection();
    }

    private void CopyText(string text, string done)
    {
        try
        {
            clipboard.SetText(text);
            notifier.Info(done);
        }
        catch (BusinessException ex)
        {
            notifier.Error(ex.Message);
        }
    }

    private void OpenWhatsApp(string? url)
    {
        if (url is null)
        {
            notifier.Error("Numéro de téléphone du parent manquant ou invalide");
            return;
        }
        try
        {
            launcher.OpenUrl(url);
        }
        catch (BusinessException ex)
        {
            notifier.Error(ex.Message);
        }
    }

    private List<ReminderLine>? RequireSelection()
    {
        var list = SelectedReminders;
        if (list.Count > 0) return list;
        notifier.Info(Reminders.Count == 0 ? "Aucune séance impayée" : "Sélectionnez au moins un élève");
        return null;
    }

    /// <summary>Every selected reminder, each preceded by the student, parent and phone (to paste in a notepad or e-mail).</summary>
    [RelayCommand]
    private void CopyAllReminders()
    {
        if (RequireSelection() is not { } list) return;
        var text = string.Join("\n\n", list.Select(l => $"— {l.Name} · {l.Parent} · {l.Phone}\n{l.Message}"));
        CopyText(text, $"{list.Count} relance{(list.Count > 1 ? "s" : "")} copiée{(list.Count > 1 ? "s" : "")}");
    }

    /// <summary>One reminder letter per selected student, in a single print job.</summary>
    [RelayCommand]
    private async Task PrintReminderLetters()
    {
        if (RequireSelection() is not { } list) return;
        var today = clock.GetLocalNow().DateTime.Date;
        await RunAsync(() =>
        {
            var cfg = _settings;
            var pages = list.Select(l => new PrintPage("Rappel de paiement", Owed(l.Item),
            [
                new PrintParagraph($"Le {today:dd/MM/yyyy}", Muted: true, AlignRight: true),
                new PrintParagraph($"À l'attention de {l.ParentName ?? MessageTemplates.NoParentGreeting}", Bold: true),
                new PrintParagraph($"Parent de {l.Name} ({l.Details})", Muted: true),
                new PrintSpacer(),
                new PrintParagraph(l.Message),
                new PrintSpacer(),
                new PrintFields(
                [
                    ("Élève", l.Name),
                    .. l.Item.GroupAccounts.Where(a => a.Balance > 0).Select(a => (a.Group, "Reste " + Money.Format(a.Balance))),
                    ("Séances facturées", Money.Format(l.Item.Due)),
                    ("Déjà réglé", Money.Format(l.Item.Paid)),
                    ("Reste à payer", Money.Format(l.Item.Balance)),
                    ("Échéance", DueDate(l.Item).ToString("dd/MM/yyyy")),
                ]),
                .. string.IsNullOrWhiteSpace(cfg.PaymentRulesNote) ? Array.Empty<PrintBlock>() : [new PrintSpacer(8), new PrintParagraph(cfg.PaymentRulesNote, Muted: true, Size: 10.5)],
                new PrintSpacer(24),
                new PrintSignature("La direction"),
            ])).ToList();
            printer.PrintPages($"Relances {today:yyyy-MM-dd}", cfg, cfg.LogoFile is null ? null : storage.GetPath(cfg.LogoFile, StorageAreas.Images), pages);
            return Task.CompletedTask;
        }, notifier);
    }

    [RelayCommand]
    private async Task ExportReminders()
    {
        if (RequireSelection() is not { } list) return;
        var today = clock.GetLocalNow().DateTime;
        var path = files.SaveFile("Exporter les relances", $"relances-{today:yyyy-MM-dd}.xlsx", "Classeur Excel|*.xlsx");
        if (path is null) return;
        await RunAsync(async () =>
        {
            await export.ExportTableAsync(path, "Relances " + today.ToString("yyyy-MM-dd"),
                ["Matricule", "Élève", "Niveau", "Parent", "Téléphone", "Numéro WhatsApp", "Séances facturées", "Payé", "Reste", "Jours de retard", "Message"],
                list.Select(l => (IReadOnlyList<object?>)[l.Item.Matricule, l.Name, l.Item.Level, l.ParentName, l.Item.ParentPhone,
                    MessageTemplates.NormalizePhone(l.Item.ParentPhone, _settings.PhoneCountryCode), l.Item.Due, l.Item.Paid, l.Item.Balance,
                    Math.Max(0, l.DaysLate), l.Message]));
            notifier.Info("Export Excel généré");
            shell.Reveal(path);
        }, notifier);
    }

    /// <summary>Optional navigation parameter: open a given month and/or tab ("monthly", "receipts", "discounts", "reminders").</summary>
    public sealed record Target(DateTime? Month = null, string? Tab = null);
}
