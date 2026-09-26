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

/// <summary>One student line of the monthly payments table.</summary>
public sealed record PayLine(PaymentRow Item, IRelayCommand Open, IRelayCommand Collect)
{
    public int StudentId => Item.StudentId;
    public string Name => Item.FullName;
    /// <summary>" · −10 %" when a discount applies (muted after the name).</summary>
    public string Discount => Item.Discount is null ? "" : " · " + DiscountValue(Item.Discount);
    public string Due => Money.Format(Item.Due);
    public string Paid => Money.Format(Item.Paid);
    public string Rest => Money.Format(Item.Balance);
    public decimal BalanceValue => Item.Balance;
    public Badge State => Badge.For(Item.State);
    public bool CanCollect => Item.Balance > 0;

    // Discount.ToString() is "Name (−10 %)": keep the value only.
    private static string DiscountValue(string label)
    {
        var i = label.LastIndexOf('(');
        return i >= 0 && label.EndsWith(')') ? label[(i + 1)..^1] : label;
    }
}

/// <summary>Compact receipt entry of the "Derniers reçus" column.</summary>
public sealed record PayRecentReceipt(string Name, string Details, string Amount, IRelayCommand Print);

/// <summary>Receipt line of the "Reçus" tab.</summary>
public sealed record PayReceiptLine(int Id, string Number, string Date, string Student, string Kind, string Month, string Method, string Amount,
    IRelayCommand Print, IRelayCommand Delete);

/// <summary>Discount line of the "Remises" tab.</summary>
public sealed record DiscountLine(int Id, string Name, string Type, string Value, Badge Active, string Students, string? Notes, IRelayCommand Edit, IRelayCommand Delete);

public sealed partial class PaymentsViewModel(
    IPaymentService payments, IStudentService students, ICrudService<Discount> discounts, ISettingsService settings, IFileStorage storage,
    IPrintService printer, IExportService export, IFilePicker files, IShell shell,
    INavigator nav, DialogHost dialogs, INotifier notifier, TimeProvider clock, IServiceProvider services) : PageViewModel
{
    private List<PaymentRow> _all = [];

    public override string NavKey => "payments";
    public override string Title => "Paiements des élèves";

    [ObservableProperty] private IReadOnlyList<Option<DateTime>> _months = [];
    [ObservableProperty] private Option<DateTime>? _selectedMonth;
    [ObservableProperty] private string _monthLabel = "";
    [ObservableProperty] private string _tab = "monthly";

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

    public bool IsMonthly => Tab == "monthly";
    public bool IsReceipts => Tab == "receipts";
    public bool IsDiscounts => Tab == "discounts";

    partial void OnTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsMonthly));
        OnPropertyChanged(nameof(IsReceipts));
        OnPropertyChanged(nameof(IsDiscounts));
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
            SelectedMonth = Months.First(m => m.Value == Period.Of(parameter is DateTime d ? d : now));
        }
        await ReloadAsync();
    }

    private Task ReloadAsync() => RunAsync(async () =>
    {
        var month = Month;
        MonthLabel = Labels.Month(month);
        _all = await payments.MonthOverviewAsync(month);
        var due = _all.Sum(r => r.Due);
        var paid = _all.Sum(r => Math.Min(r.Due, r.Paid));
        var rest = _all.Sum(r => r.Balance);
        var late = _all.Count(r => r.Balance > 0);
        Kpis =
        [
            new("Attendu ce mois", Money.Format(due), $"{_all.Count(r => r.Due > 0)} élèves facturés"),
            new("Encaissé", Money.Format(paid), "Mensualités du mois"),
            new("Reste à percevoir", Money.Format(rest), $"{late} élève{(late > 1 ? "s" : "")} concerné{(late > 1 ? "s" : "")}"),
            new("Taux de recouvrement", $"{Math.Round(paid * 100 / Math.Max(1, due))} %", MonthLabel),
        ];
        ApplyFilter();
        await LoadReceiptsAsync();
        await LoadDiscountsAsync();
    }, notifier);

    private void ApplyFilter()
    {
        var q = SearchText.Trim().ToLowerInvariant();
        var list = _all
            .Where(r => SelectedFilter == "Tous" || Labels.Of(r.State) == SelectedFilter)
            .Where(r => q.Length == 0 || $"{r.FullName} {r.Matricule} {r.ParentPhone}".ToLowerInvariant().Contains(q))
            .ToList();
        Rows = list.Select(r => new PayLine(r,
            new AsyncRelayCommand(() => nav.NavigateAsync<StudentDetailViewModel>(r.StudentId)),
            new AsyncRelayCommand(() => CollectAsync(r.StudentId)))).ToList();
        CountLabel = $"{list.Count} élève{(list.Count > 1 ? "s" : "")} sur {_all.Count}";
    }

    private async Task LoadReceiptsAsync()
    {
        var month = Month;
        var list = await payments.ReceiptsAsync(month, Period.End(month));
        LatestReceipts = list.Take(12).Select(p => new PayRecentReceipt(
            p.Student?.FullName ?? "—", $"{p.ReceiptNumber} · {p.Date:dd/MM} · {Labels.Of(p.Method)}", Money.Format(p.Amount),
            new AsyncRelayCommand(() => PrintReceiptAsync(p.Id)))).ToList();
        Receipts = list.Select(p => new PayReceiptLine(p.Id, p.ReceiptNumber, p.Date.ToString("dd/MM/yyyy"), p.Student?.FullName ?? "—",
            Labels.Of(p.Kind), Labels.Month(p.Period), Labels.Of(p.Method), Money.Format(p.Amount),
            new AsyncRelayCommand(() => PrintReceiptAsync(p.Id)),
            new AsyncRelayCommand(() => DeleteReceiptAsync(p)))).ToList();
        ReceiptsTotal = $"{list.Count} reçu{(list.Count > 1 ? "s" : "")} · {Money.Format(list.Sum(p => p.Amount))}";
    }

    private async Task LoadDiscountsAsync()
    {
        var all = await discounts.ListAsync();
        var studentList = await students.ListAsync(Month);
        DiscountLines = all.Select(d =>
        {
            var n = studentList.Count(s => s.IsActive && s.DiscountLabel == d.ToString());
            return new DiscountLine(d.Id, d.Name, Labels.Of(d.Type), d.ValueLabel, Badge.Active(d.IsActive),
                n == 0 ? "Aucun élève" : $"{n} élève{(n > 1 ? "s" : "")}", d.Notes,
                new AsyncRelayCommand(() => EditDiscountAsync(d)),
                new AsyncRelayCommand(() => DeleteDiscountAsync(d, n)));
        }).ToList();
    }

    [RelayCommand]
    private async Task Collect()
    {
        var dialog = services.GetRequiredService<CollectPaymentDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(null), notifier)) return;
        SelectMonth(dialog);
        if (await dialogs.ShowAsync(dialog)) await ReloadAsync();
    }

    private async Task CollectAsync(int studentId)
    {
        var dialog = services.GetRequiredService<CollectPaymentDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(studentId), notifier)) return;
        SelectMonth(dialog);
        if (await dialogs.ShowAsync(dialog)) await ReloadAsync();
    }

    private void SelectMonth(CollectPaymentDialogViewModel dialog)
    {
        var month = Month;
        if (dialog.SelectedMonth?.Value != month && dialog.Months.FirstOrDefault(m => m.Value == month) is { } option)
            dialog.SelectedMonth = option;
    }

    private async Task PrintReceiptAsync(int paymentId)
    {
        await RunAsync(async () =>
        {
            var p = await payments.GetReceiptAsync(paymentId) ?? throw new BusinessException("Reçu introuvable.");
            var cfg = await settings.GetAsync();
            printer.PrintReceipt(p, cfg, cfg.LogoFile is null ? null : storage.GetPath(cfg.LogoFile, StorageAreas.Images));
        }, notifier);
    }

    private async Task DeleteReceiptAsync(StudentPayment p)
    {
        if (!await dialogs.ConfirmAsync("Annuler le paiement",
                $"Supprimer le paiement {p.ReceiptNumber} de {Money.Format(p.Amount)} ({p.Student?.FullName}) ? Le reçu ne sera plus valable.", "Supprimer le paiement"))
            return;
        await RunAsync(async () =>
        {
            await payments.DeleteAsync(p.Id);
            notifier.Info("Paiement supprimé");
        }, notifier);
        await ReloadAsync();
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
        var month = Month;
        var path = files.SaveFile("Exporter les paiements", $"paiements-{month:yyyy-MM}.xlsx", "Classeur Excel|*.xlsx");
        if (path is null) return;
        await RunAsync(async () =>
        {
            await export.ExportTableAsync(path, "Paiements " + month.ToString("yyyy-MM"),
                ["Matricule", "Élève", "Niveau", "Remise", "Mensualité", "Payé", "Reste", "Statut", "Téléphone parent"],
                _all.Select(r => (IReadOnlyList<object?>)[r.Matricule, r.FullName, r.Level, r.Discount, r.Due, r.Paid, r.Balance, Labels.Of(r.State), r.ParentPhone]));
            notifier.Info("Export Excel généré");
            shell.Reveal(path);
        }, notifier);
    }
}
