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

public sealed record ExpenseLine(int Id, string Date, string Category, string Description, string Supplier, string Method, string Amount, decimal AmountValue,
    IRelayCommand Edit, IRelayCommand Delete);

public sealed partial class ExpensesViewModel(
    ICrudService<Expense> expenses, IExportService export, IFilePicker files, IShell shell,
    DialogHost dialogs, INotifier notifier, TimeProvider clock, IServiceProvider services) : PageViewModel
{
    private const string AllCategories = "Toutes";
    private List<Expense> _month = [];

    public override string NavKey => "expenses";
    public override string Title => "Dépenses";

    [ObservableProperty] private IReadOnlyList<Option<DateTime>> _months = [];
    [ObservableProperty] private Option<DateTime>? _selectedMonth;
    [ObservableProperty] private string _monthLabel = "";
    [ObservableProperty] private IReadOnlyList<Kpi> _kpis = [];
    [ObservableProperty] private IReadOnlyList<string> _categories = [AllCategories];
    [ObservableProperty] private string _selectedCategory = AllCategories;
    [ObservableProperty] private IReadOnlyList<ExpenseLine> _rows = [];
    [ObservableProperty] private string _totalLabel = "";

    private DateTime Month => SelectedMonth?.Value ?? Period.Of(clock.GetLocalNow().DateTime);

    async partial void OnSelectedMonthChanged(Option<DateTime>? oldValue, Option<DateTime>? newValue)
    {
        if (oldValue is not null && newValue is not null) await ReloadAsync();
    }

    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    public override async Task LoadAsync(object? parameter)
    {
        if (Months.Count == 0)
        {
            var now = clock.GetLocalNow().DateTime;
            Months = Options.Months(now);
            SelectedMonth = Months.First(m => m.Value == Period.Of(now));
        }
        await ReloadAsync();
    }

    private Task ReloadAsync() => RunAsync(async () =>
    {
        var month = Month;
        MonthLabel = Labels.Month(month);
        var end = Period.End(month);
        _month = (await expenses.ListAsync()).Where(e => e.Date.Date >= month && e.Date.Date <= end).ToList();
        var total = _month.Sum(e => e.Amount);
        var biggest = _month.GroupBy(e => e.Category).Select(g => (Category: g.Key, Amount: g.Sum(e => e.Amount)))
            .OrderByDescending(x => x.Amount).FirstOrDefault();
        Kpis =
        [
            new("Total du mois", Money.Format(total), MonthLabel),
            new("Nombre de dépenses", _month.Count.ToString(), _month.Count == 0 ? "Aucune dépense" : $"Moyenne {Money.Format(total / _month.Count)}"),
            new("Poste le plus important", biggest.Category ?? "—", biggest.Category is null ? "" : Money.Format(biggest.Amount)),
        ];
        var used = _month.Select(e => e.Category).Distinct().ToList();
        Categories = [AllCategories, .. Options.ExpenseCategories.Where(used.Contains), .. used.Except(Options.ExpenseCategories).Order()];
        if (!Categories.Contains(SelectedCategory)) SelectedCategory = AllCategories;
        ApplyFilter();
    }, notifier);

    private void ApplyFilter()
    {
        var list = _month.Where(e => SelectedCategory == AllCategories || e.Category == SelectedCategory).ToList();
        Rows = list.Select(e => new ExpenseLine(e.Id, e.Date.ToString("dd/MM/yyyy"), e.Category, string.IsNullOrWhiteSpace(e.Description) ? "—" : e.Description,
            e.Supplier ?? "—", Labels.Of(e.Method), Money.Format(e.Amount), e.Amount,
            new AsyncRelayCommand(() => EditAsync(e)),
            new AsyncRelayCommand(() => DeleteAsync(e)))).ToList();
        TotalLabel = $"{list.Count} dépense{(list.Count > 1 ? "s" : "")} · {Money.Format(list.Sum(e => e.Amount))}";
    }

    [RelayCommand]
    private Task Add() => EditAsync(null);

    private async Task EditAsync(Expense? e)
    {
        var dialog = services.GetRequiredService<ExpenseEditorDialogViewModel>();
        var today = clock.GetLocalNow().DateTime;
        dialog.Initialize(e, Period.Of(today) == Month ? today : Month);
        if (!await dialogs.ShowAsync(dialog)) return;
        notifier.Info(e is null ? "Dépense ajoutée" : "Dépense enregistrée");
        await ReloadAsync();
    }

    private async Task DeleteAsync(Expense e)
    {
        if (!await dialogs.ConfirmAsync("Supprimer la dépense", $"Supprimer la dépense « {e.Category} » de {Money.Format(e.Amount)} du {e.Date:dd/MM/yyyy} ?")) return;
        if (await RunAsync(() => expenses.DeleteAsync(e.Id), notifier))
        {
            notifier.Info("Dépense supprimée");
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private async Task Export()
    {
        var month = Month;
        var path = files.SaveFile("Exporter les dépenses", $"depenses-{month:yyyy-MM}.xlsx", "Classeur Excel|*.xlsx");
        if (path is null) return;
        await RunAsync(async () =>
        {
            await export.ExportTableAsync(path, "Dépenses " + month.ToString("yyyy-MM"),
                ["Date", "Catégorie", "Description", "Fournisseur", "Mode", "Montant"],
                _month.Select(e => (IReadOnlyList<object?>)[e.Date, e.Category, e.Description, e.Supplier, Labels.Of(e.Method), e.Amount]));
            notifier.Info("Export Excel généré");
            shell.Reveal(path);
        }, notifier);
    }
}
