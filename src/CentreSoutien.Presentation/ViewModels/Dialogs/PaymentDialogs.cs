using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CentreSoutien.Presentation.ViewModels.Dialogs;

/// <summary>Collect a student payment and generate its receipt.</summary>
public sealed partial class CollectPaymentDialogViewModel(
    IPaymentService payments, ISettingsService settings, IFileStorage storage, IPrintService printer, INotifier notifier, TimeProvider clock) : DialogViewModel
{
    private List<PaymentRow> _rows = [];

    public override string Title => "Encaisser un paiement";
    public override string ConfirmText => "Valider et générer le reçu";

    public bool CanPickStudent { get; private set; }
    public IReadOnlyList<Option<PaymentMethod>> Methods => Options.PaymentMethods;
    public IReadOnlyList<Option<PaymentKind>> Kinds => Options.PaymentKinds;

    [ObservableProperty] private IReadOnlyList<Option<int>> _students = [];
    [ObservableProperty] private Option<int>? _selectedStudent;
    [ObservableProperty] private string _studentName = "";
    [ObservableProperty] private IReadOnlyList<Option<DateTime>> _months = [];
    [ObservableProperty] private Option<DateTime>? _selectedMonth;
    [ObservableProperty] private Option<PaymentKind>? _selectedKind;
    [ObservableProperty] private Option<PaymentMethod>? _selectedMethod;
    [ObservableProperty] private string _amount = "";
    [ObservableProperty] private string? _note;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _printReceipt = true;

    public StudentPaymentResult? Result { get; private set; }

    public async Task InitializeAsync(int? studentId)
    {
        var now = clock.GetLocalNow().DateTime;
        Months = Options.Months(now);
        SelectedMonth = Months.First(m => m.Value == Period.Of(now));
        SelectedKind = Kinds[0];
        SelectedMethod = Methods[0];
        await LoadRowsAsync();
        CanPickStudent = studentId is null;
        Students = _rows.Select(r => new Option<int>(r.StudentId, r.Balance > 0 ? $"{r.FullName} — reste {Money.Format(r.Balance)}" : $"{r.FullName} — à jour")).ToList();
        SelectedStudent = studentId is null ? null : Students.FirstOrDefault(s => s.Value == studentId);
        if (studentId is not null && SelectedStudent is null)
            throw new BusinessException("Cet élève est inactif : réactivez-le pour encaisser une mensualité.");
        OnPropertyChanged(nameof(CanPickStudent));
        UpdateSuggestion();
    }

    private async Task LoadRowsAsync() => _rows = await payments.MonthOverviewAsync(SelectedMonth?.Value ?? Period.Current);

    partial void OnSelectedStudentChanged(Option<int>? value) => UpdateSuggestion();
    partial void OnSelectedKindChanged(Option<PaymentKind>? value) => UpdateSuggestion();

    async partial void OnSelectedMonthChanged(Option<DateTime>? value)
    {
        if (value is null || Students.Count == 0) return;
        await LoadRowsAsync();
        UpdateSuggestion();
    }

    private async void UpdateSuggestion()
    {
        var row = _rows.FirstOrDefault(r => r.StudentId == SelectedStudent?.Value);
        StudentName = row?.FullName ?? "";
        if (row is null)
        {
            Summary = SelectedMonth?.Label ?? "";
            return;
        }
        if (SelectedKind?.Value == PaymentKind.Registration)
        {
            var s = await settings.GetAsync();
            Amount = Money.Number(s.RegistrationFee);
            Summary = "Frais d'inscription";
            return;
        }
        Amount = row.Balance > 0 ? Money.Number(row.Balance) : "";
        Summary = $"{SelectedMonth?.Label} · mensualité {Money.Format(row.Due)} · payé {Money.Format(row.Paid)} · reste {Money.Format(row.Balance)}"
            + (row.Discount is null ? "" : $" · remise {row.Discount} incluse");
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        if (SelectedStudent is null) throw new BusinessException("Choisissez un élève.");
        var amount = Parse.Amount(Amount) ?? 0;
        if (amount <= 0) throw new BusinessException("Saisissez un montant.");
        var p = await payments.RecordAsync(SelectedStudent.Value, amount, SelectedMethod!.Value, SelectedKind!.Value, SelectedMonth!.Value, Note);
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
