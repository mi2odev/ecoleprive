using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Presentation.ViewModels.Dialogs;

/// <summary>Chip of the payment dialog that fills the amount ("Solde restant · 3 000 DZD").</summary>
public sealed record QuickAmountOption(string Label, decimal Value, IRelayCommand Apply)
{
    public string Text => $"{Label} · {Money.Format(Value)}";
}

/// <summary>
/// Collect a student payment and generate its receipt. Each group is paid on its own, by packs of sessions: pick the
/// group, the amount proposed is what is owed for it, or its next pack when it is paid up.
/// </summary>
public sealed partial class CollectPaymentDialogViewModel(
    IPaymentService payments, ISettingsService settings, IFileStorage storage, IPrintService printer, INotifier notifier) : DialogViewModel
{
    private List<PaymentRow> _rows = [];
    private decimal? _registrationFee;

    public override string Title => "Encaisser un paiement";
    public override string ConfirmText => "Valider et générer le reçu";

    public bool CanPickStudent { get; private set; }
    public IReadOnlyList<Option<PaymentMethod>> Methods => Options.PaymentMethods;
    public IReadOnlyList<Option<PaymentKind>> Kinds => Options.PaymentKinds;

    [ObservableProperty] private IReadOnlyList<Option<int>> _students = [];
    [ObservableProperty] private Option<int>? _selectedStudent;
    [ObservableProperty] private string _studentName = "";
    /// <summary>The student's groups, each with what is left to pay for it.</summary>
    [ObservableProperty] private IReadOnlyList<Option<int>> _groups = [];
    [ObservableProperty] private Option<int>? _selectedGroup;
    [ObservableProperty] private Option<PaymentKind>? _selectedKind;
    [ObservableProperty] private Option<PaymentMethod>? _selectedMethod;
    [ObservableProperty] private string _amount = "";
    [ObservableProperty] private string? _note;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _printReceipt = true;
    /// <summary>"reste 3 000 DZD" / "à jour" next to the student's name (all groups).</summary>
    [ObservableProperty] private string _studentBalance = "";
    /// <summary>Chips that fill the amount: left to pay for the group, its next pack, registration fee.</summary>
    [ObservableProperty] private IReadOnlyList<QuickAmountOption> _quickAmounts = [];
    /// <summary>"Reste après ce paiement : 1 500 DZD" for the chosen group, updated while typing.</summary>
    [ObservableProperty] private string _remainingAfter = "";

    public bool HasQuickAmounts => QuickAmounts.Count > 0;
    public bool HasRemainingAfter => RemainingAfter.Length > 0;
    /// <summary>Session payment: the group must be chosen.</summary>
    public bool IsSessions => SelectedKind?.Value == PaymentKind.Sessions;
    partial void OnQuickAmountsChanged(IReadOnlyList<QuickAmountOption> value) => OnPropertyChanged(nameof(HasQuickAmounts));
    partial void OnRemainingAfterChanged(string value) => OnPropertyChanged(nameof(HasRemainingAfter));
    partial void OnAmountChanged(string value) => UpdateRemainingAfter();

    public StudentPaymentResult? Result { get; private set; }

    /// <param name="studentId">Student paying, or null to pick one.</param>
    /// <param name="groupId">Group paid for (default: the first group with something to pay).</param>
    public async Task InitializeAsync(int? studentId, int? groupId = null)
    {
        SelectedKind = Kinds[0];
        SelectedMethod = Methods[0];
        _registrationFee = (await settings.GetAsync()).RegistrationFee;
        _rows = await payments.OverviewAsync();
        CanPickStudent = studentId is null;
        Students = _rows.Select(r => new Option<int>(r.StudentId, r.Balance > 0 ? $"{r.FullName} — reste {Money.Format(r.Balance)}" : $"{r.FullName} — à jour")).ToList();
        SelectedStudent = studentId is null ? null : Students.FirstOrDefault(s => s.Value == studentId);
        if (studentId is not null && SelectedStudent is null)
            throw new BusinessException("Cet élève est inactif : réactivez-le pour encaisser un paiement.");
        if (groupId is not null && Groups.FirstOrDefault(g => g.Value == groupId) is { } group) SelectedGroup = group;
        OnPropertyChanged(nameof(CanPickStudent));
        UpdateSuggestion();
    }

    private PaymentRow? Row => _rows.FirstOrDefault(r => r.StudentId == SelectedStudent?.Value);
    private GroupAccount? Account => Row?.GroupAccounts.FirstOrDefault(a => a.GroupId == SelectedGroup?.Value);

    partial void OnSelectedStudentChanged(Option<int>? value)
    {
        var row = Row;
        var accounts = row?.GroupAccounts ?? [];
        Groups = accounts.Select(a => new Option<int>(a.GroupId, GroupLabel(a))).ToList();
        var first = accounts.FirstOrDefault(a => a.Balance > 0) ?? accounts.FirstOrDefault(a => a.Status is not null) ?? accounts.FirstOrDefault();
        SelectedGroup = first is null ? null : Groups.First(g => g.Value == first.GroupId);
        UpdateSuggestion();
    }

    private static string GroupLabel(GroupAccount a) => string.Join(" — ", new[]
    {
        a.Group,
        a.Balance > 0 ? $"reste {Money.Format(a.Balance)}" : a.Credit > 0 ? $"{Money.Format(a.Credit)} d'avance" : "à jour",
        a.Status is { } st ? $"séance {Math.Min(st.Done + 1, st.Size)}/{st.Size}" : "a quitté le groupe",
    });

    partial void OnSelectedGroupChanged(Option<int>? value) => UpdateSuggestion();

    partial void OnSelectedKindChanged(Option<PaymentKind>? value)
    {
        OnPropertyChanged(nameof(IsSessions));
        UpdateSuggestion();
    }

    private void UpdateSuggestion()
    {
        var row = Row;
        StudentName = row?.FullName ?? "";
        StudentBalance = row is null ? "" : row.Balance > 0 ? $"reste {Money.Format(row.Balance)}" : row.Credit > 0 ? $"{Money.Format(row.Credit)} d'avance" : "à jour";
        var account = Account;
        QuickAmounts = row is null ? [] : BuildQuickAmounts(account);
        if (row is null)
        {
            Summary = "";
            UpdateRemainingAfter();
            return;
        }
        if (SelectedKind?.Value == PaymentKind.Registration)
        {
            Amount = _registrationFee is > 0 ? Money.Number(_registrationFee.Value) : "";
            Summary = "Frais d'inscription";
            UpdateRemainingAfter();
            return;
        }
        if (SelectedKind?.Value != PaymentKind.Sessions)
        {
            Summary = "";
            UpdateRemainingAfter();
            return;
        }
        if (account is null)
        {
            Amount = "";
            Summary = row.GroupAccounts.Count == 0 ? "Cet élève n'est inscrit dans aucun groupe." : "Choisissez le groupe payé.";
            UpdateRemainingAfter();
            return;
        }
        // Owes something for this group: propose it. Up to date: propose its next pack (paid in advance).
        Amount = account.Balance > 0 ? Money.Number(account.Balance) : account.PackPrice > 0 ? Money.Number(account.PackPrice) : "";
        Summary = string.Join(" · ", new[]
        {
            account.Status is { } st ? $"Séance {Math.Min(st.Done + 1, st.Size)}/{st.Size} du paquet {st.Pack}" : "A quitté le groupe",
            $"{Money.Format(account.PackPrice)} les {(account.Status?.Size is { } n ? n + " séances" : "séances")}",
            account.Balance > 0 ? $"reste {Money.Format(account.Balance)}" : "séances payées",
            row.Discount is null ? null : $"remise {row.Discount} incluse",
        }.Where(x => x is not null));
        UpdateRemainingAfter();
    }

    private List<QuickAmountOption> BuildQuickAmounts(GroupAccount? account)
    {
        var list = new List<QuickAmountOption>();
        if (account is not null)
        {
            if (account.Balance > 0) list.Add(new("Reste à payer", account.Balance, new RelayCommand(() => UseSessionsAmount(account.Balance))));
            if (account.PackPrice > 0 && account.PackPrice != account.Balance && account.Status is not null)
                list.Add(new("Prochaines séances", account.PackPrice, new RelayCommand(() => UseSessionsAmount(account.PackPrice))));
        }
        if (_registrationFee is { } fee && fee > 0) list.Add(new("Frais d'inscription", fee, new RelayCommand(UseRegistrationFee)));
        return list;
    }

    private void UseSessionsAmount(decimal value)
    {
        if (SelectedKind?.Value != PaymentKind.Sessions) SelectedKind = Kinds.First(k => k.Value == PaymentKind.Sessions);
        Amount = Money.Number(value);
    }

    /// <summary>Chip "Frais d'inscription": switches the payment to a registration fee.</summary>
    private void UseRegistrationFee()
    {
        var registration = Kinds.First(k => k.Value == PaymentKind.Registration);
        if (SelectedKind != registration) SelectedKind = registration;
        else if (_registrationFee is { } fee) Amount = Money.Number(fee);
    }

    private void UpdateRemainingAfter()
    {
        var account = Account;
        if (account is null || SelectedKind?.Value != PaymentKind.Sessions)
        {
            RemainingAfter = "";
            return;
        }
        var rest = account.Balance - (Parse.Amount(Amount) ?? 0);
        RemainingAfter = rest >= 0
            ? $"Reste après ce paiement : {Money.Format(rest)}"
            : $"Reste après ce paiement : {Money.Format(0)} · {Money.Format(-rest)} payés d'avance";
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        if (SelectedStudent is null) throw new BusinessException("Choisissez un élève.");
        var sessions = SelectedKind?.Value == PaymentKind.Sessions;
        if (sessions && SelectedGroup is null) throw new BusinessException("Choisissez le groupe payé.");
        var amount = Parse.Amount(Amount) ?? 0;
        if (amount <= 0) throw new BusinessException("Saisissez un montant.");
        var p = await payments.RecordAsync(SelectedStudent.Value, sessions ? SelectedGroup!.Value : null, amount, SelectedMethod!.Value, SelectedKind!.Value, Note);
        Result = new StudentPaymentResult(p.Id, p.ReceiptNumber);
        notifier.Info($"Paiement enregistré · reçu {p.ReceiptNumber} généré");
        if (PrintReceipt)
        {
            var full = await payments.GetReceiptAsync(p.Id);
            var cfg = await settings.GetAsync();
            if (full is not null) printer.PrintReceipt(full, cfg, cfg.LogoFile is null ? null : storage.GetPath(cfg.LogoFile, StorageAreas.Images));
        }
        return true;
    }
}

public sealed record StudentPaymentResult(int PaymentId, string ReceiptNumber);

/// <summary>Record a teacher payment.</summary>
public sealed partial class TeacherPaymentDialogViewModel(ITeacherPaymentService payments, INotifier notifier, TimeProvider clock) : DialogViewModel
{
    private int _teacherId;
    private List<TeacherPayRow> _rows = [];

    public override string Title => "Paiement enseignant";
    public override string ConfirmText => "Enregistrer le paiement";
    public IReadOnlyList<Option<PaymentMethod>> Methods => Options.PaymentMethods;

    [ObservableProperty] private string _teacherName = "";
    [ObservableProperty] private IReadOnlyList<Option<DateTime>> _months = [];
    [ObservableProperty] private Option<DateTime>? _selectedMonth;
    [ObservableProperty] private Option<PaymentMethod>? _selectedMethod;
    [ObservableProperty] private string _amount = "";
    [ObservableProperty] private string? _note;
    [ObservableProperty] private string _summary = "";

    public async Task InitializeAsync(int teacherId, DateTime? period = null)
    {
        _teacherId = teacherId;
        var now = clock.GetLocalNow().DateTime;
        Months = Options.Months(now);
        SelectedMethod = Methods.First(m => m.Value == PaymentMethod.Ccp);
        SelectedMonth = Months.First(m => m.Value == Period.Of(period ?? now));
        await UpdateAsync();
    }

    async partial void OnSelectedMonthChanged(Option<DateTime>? value)
    {
        if (_teacherId != 0) await UpdateAsync();
    }

    private async Task UpdateAsync()
    {
        _rows = await payments.MonthOverviewAsync(SelectedMonth!.Value);
        var row = _rows.FirstOrDefault(r => r.TeacherId == _teacherId);
        TeacherName = row?.FullName ?? "";
        Amount = row is { Remaining: > 0 } ? Money.Number(row.Remaining) : "";
        Summary = row is null ? "" : $"{SelectedMonth!.Label} · {row.Rule} · dû {Money.Format(row.Earned)} · déjà versé {Money.Format(row.Paid)}";
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        var amount = Parse.Amount(Amount) ?? 0;
        if (amount <= 0) throw new BusinessException("Saisissez un montant.");
        await payments.RecordAsync(_teacherId, amount, SelectedMethod!.Value, SelectedMonth!.Value, Note);
        notifier.Info($"Paiement de {Money.Format(amount)} enregistré pour {TeacherName}");
        return true;
    }
}

/// <summary>
/// Cancels a student receipt. A receipt is never deleted: it keeps its number, stays in the list marked "Annulé" with
/// the reason, and no longer counts in balances or revenue.
/// </summary>
public sealed partial class CancelReceiptDialogViewModel(IPaymentService payments, INotifier notifier) : DialogViewModel
{
    private int _paymentId;

    public override string Title => "Annuler un reçu";
    public override string ConfirmText => "Annuler le reçu";
    public override string CancelText => "Garder le reçu";
    public override bool IsDanger => true;
    public override double Width => 480;

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _reason = "";

    /// <summary>Usual reasons, one click fills the field.</summary>
    public IReadOnlyList<QuickAmountOption> Reasons { get; private set; } = [];

    public void Initialize(Domain.Entities.StudentPayment p)
    {
        _paymentId = p.Id;
        Summary = $"Reçu {p.ReceiptNumber} du {p.Date:dd/MM/yyyy} · {Money.Format(p.Amount)} · {p.Student?.FullName}"
                  + (p.Group is { } g ? $" · {g.FullName}" : "");
        Reasons = new[] { "Erreur de saisie", "Mauvais élève ou mauvais groupe", "Remboursement", "Doublon" }
            .Select(r => new QuickAmountOption(r, 0, new RelayCommand(() => Reason = r))).ToList();
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        if (string.IsNullOrWhiteSpace(Reason)) throw new BusinessException("Indiquez le motif de l'annulation.");
        await payments.CancelAsync(_paymentId, Reason);
        notifier.Info("Reçu annulé · il reste visible dans la liste des reçus");
        return true;
    }
}
