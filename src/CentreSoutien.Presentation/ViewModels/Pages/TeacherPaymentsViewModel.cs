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

public sealed record TeacherPayLine(TeacherPayRow Item, IRelayCommand Open, IRelayCommand Pay)
{
    public int TeacherId => Item.TeacherId;
    public string Name => Item.FullName;
    public string Subject => Item.Subject;
    public string Rule => Item.Rule;
    public string Due => Money.Format(Item.Earned);
    public string Paid => Money.Format(Item.Paid);
    public string Rest => Item.Remaining > 0 ? Money.Format(Item.Remaining) : "—";
    public decimal RemainingValue => Item.Remaining;
    public string LastPayment => Item.LastPaymentDate?.ToString("dd/MM/yyyy") ?? "—";
    public bool CanPay => Item.Remaining > 0;
    public Badge State => Item.Earned == 0 && Item.Paid == 0 ? new("Rien à verser", BadgeKind.Neutral)
        : Item.Remaining <= 0 ? new("Versé", BadgeKind.Ok)
        : Item.Paid > 0 ? new("Partiel", BadgeKind.Warn)
        : new("À verser", BadgeKind.Bad);
}

public sealed record TeacherPayHistoryLine(int Id, string Date, string Teacher, string Month, string Method, string Amount, string? Note, IRelayCommand Delete);

public sealed partial class TeacherPaymentsViewModel(
    ITeacherPaymentService payments, IExportService export, IFilePicker files, IShell shell,
    INavigator nav, DialogHost dialogs, INotifier notifier, TimeProvider clock, IServiceProvider services) : PageViewModel
{
    private List<TeacherPayRow> _rows = [];

    public override string NavKey => "tpayments";
    public override string Title => "Paiements enseignants";

    [ObservableProperty] private IReadOnlyList<Option<DateTime>> _months = [];
    [ObservableProperty] private Option<DateTime>? _selectedMonth;
    [ObservableProperty] private string _monthLabel = "";
    [ObservableProperty] private IReadOnlyList<Kpi> _kpis = [];
    [ObservableProperty] private IReadOnlyList<TeacherPayLine> _lines = [];
    [ObservableProperty] private IReadOnlyList<TeacherPayHistoryLine> _history = [];
    [ObservableProperty] private bool _historyForMonthOnly = true;
    /// <summary>No active teacher to pay: the page shows its "getting started" empty state.</summary>
    [ObservableProperty] private bool _hasNoData;
    [ObservableProperty] private bool _hasNoHistory;

    /// <summary>Empty-state shortcut: the teachers page (to add a teacher).</summary>
    [RelayCommand]
    private Task OpenTeachers() => nav.NavigateAsync<TeachersViewModel>();

    private DateTime Month => SelectedMonth?.Value ?? Period.Of(clock.GetLocalNow().DateTime);

    async partial void OnSelectedMonthChanged(Option<DateTime>? oldValue, Option<DateTime>? newValue)
    {
        if (oldValue is not null && newValue is not null) await ReloadAsync();
    }

    async partial void OnHistoryForMonthOnlyChanged(bool value) => await RunAsync(LoadHistoryAsync, notifier);

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
        _rows = await payments.MonthOverviewAsync(month);
        var due = _rows.Sum(r => r.Earned);
        var paid = _rows.Sum(r => r.Paid);
        var rest = _rows.Sum(r => r.Remaining);
        var pending = _rows.Count(r => r.Remaining > 0);
        Kpis =
        [
            new("Total dû", Money.Format(due), $"{_rows.Count} enseignant{(_rows.Count > 1 ? "s" : "")} actif{(_rows.Count > 1 ? "s" : "")}"),
            new("Versé", Money.Format(paid), MonthLabel),
            new("Reste à verser", Money.Format(rest), pending == 0 ? "Tout est réglé" : $"{pending} enseignant{(pending > 1 ? "s" : "")} à payer"),
        ];
        Lines = _rows.Select(r => new TeacherPayLine(r,
            new AsyncRelayCommand(() => nav.NavigateAsync<TeacherDetailViewModel>(r.TeacherId)),
            new AsyncRelayCommand(() => PayAsync(r.TeacherId)))).ToList();
        HasNoData = _rows.Count == 0;
        await LoadHistoryAsync();
    }, notifier);

    private async Task LoadHistoryAsync()
    {
        var month = Month;
        var list = await payments.HistoryAsync();
        if (HistoryForMonthOnly) list = list.Where(p => Period.Of(p.Period) == month).ToList();
        History = list.Select(p => new TeacherPayHistoryLine(p.Id, p.Date.ToString("dd/MM/yyyy"), p.Teacher?.FullName ?? "—",
            Labels.Month(p.Period), Labels.Of(p.Method), Money.Format(p.Amount), p.Note,
            new AsyncRelayCommand(() => DeleteAsync(p)))).ToList();
        HasNoHistory = History.Count == 0;
    }

    private async Task PayAsync(int teacherId)
    {
        var dialog = services.GetRequiredService<TeacherPaymentDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(teacherId, Month), notifier)) return;
        if (await dialogs.ShowAsync(dialog)) await ReloadAsync();
    }

    private async Task DeleteAsync(TeacherPayment p)
    {
        if (!await dialogs.ConfirmAsync("Supprimer le paiement",
                $"Supprimer le versement de {Money.Format(p.Amount)} à {p.Teacher?.FullName} ({Labels.Month(p.Period)}) ?"))
            return;
        if (await RunAsync(() => payments.DeleteAsync(p.Id), notifier))
        {
            notifier.Info("Paiement supprimé");
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private async Task Export()
    {
        var month = Month;
        var path = files.SaveFile("Exporter les paiements enseignants", $"paiements-enseignants-{month:yyyy-MM}.xlsx", "Classeur Excel|*.xlsx");
        if (path is null) return;
        await RunAsync(async () =>
        {
            await export.ExportTableAsync(path, "Enseignants " + month.ToString("yyyy-MM"),
                ["Enseignant", "Matière", "Règle", "Dû", "Versé", "Reste", "Dernier paiement"],
                _rows.Select(r => (IReadOnlyList<object?>)[r.FullName, r.Subject, r.Rule, r.Earned, r.Paid, r.Remaining, r.LastPaymentDate]));
            notifier.Info("Export Excel généré");
            shell.Reveal(path);
        }, notifier);
    }
}
