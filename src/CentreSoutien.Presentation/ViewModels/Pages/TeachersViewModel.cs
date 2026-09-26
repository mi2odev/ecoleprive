using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Presentation.ViewModels.Pages;

public sealed record TeacherListRow(TeacherListItem Item, IRelayCommand Open)
{
    public int Id => Item.Id;
    public string Name => Item.FullName;
    public string Initials => Item.Initials;
    public string Phone => Item.Phone ?? "—";
    public string Subject => Item.Subject;
    public int Groups => Item.GroupCount;
    public int Students => Item.StudentCount;
    public string Rule => Item.Rule;
    public string Earnings => Item.MonthEarnings > 0 ? Money.Format(Item.MonthEarnings) : "—";
    public Badge Status => Badge.Active(Item.IsActive);
    public Badge? PayState => TeachersViewModel.PayBadge(Item.MonthEarnings, Item.PaidThisMonth);
}

public sealed partial class TeachersViewModel(
    ITeacherService teachers, INavigator nav, DialogHost dialogs, INotifier notifier, IFilePicker files, IExportService export,
    IShell shell, TimeProvider clock, IServiceProvider services) : PageViewModel, IHasPrimaryAction
{
    private List<TeacherListItem> _all = [];

    public override string NavKey => "teachers";
    public override string Title => "Enseignants";

    public IReadOnlyList<string> Filters { get; } = ["Tous", "Actifs", "Inactifs"];

    [ObservableProperty] private string _selectedFilter = "Tous";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private IReadOnlyList<TeacherListRow> _rows = [];
    [ObservableProperty] private string _countLabel = "";
    [ObservableProperty] private string _monthLabel = "";
    /// <summary>Nothing recorded yet: the page shows its "getting started" empty state.</summary>
    [ObservableProperty] private bool _hasNoData;
    /// <summary>There is data, but the current search / filters hide all of it.</summary>
    [ObservableProperty] private bool _hasNoResults;

    partial void OnSelectedFilterChanged(string value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>"Payé" / "À payer" for the month; null when nothing is owed.</summary>
    public static Badge? PayBadge(decimal earned, bool paid) =>
        earned <= 0 ? null : paid ? new Badge("Payé", BadgeKind.Ok) : new Badge("À payer", BadgeKind.Warn);

    public override async Task LoadAsync(object? parameter)
    {
        await RunAsync(async () =>
        {
            var now = clock.GetLocalNow().DateTime;
            MonthLabel = Labels.Months[now.Month - 1];
            _all = await teachers.ListAsync(now);
            ApplyFilter();
        }, notifier);
    }

    private void ApplyFilter()
    {
        var q = SearchText.Trim().ToLowerInvariant();
        var list = _all
            .Where(t => SelectedFilter switch { "Actifs" => t.IsActive, "Inactifs" => !t.IsActive, _ => true })
            .Where(t => q.Length == 0 || $"{t.FullName} {t.Subject} {t.Phone}".ToLowerInvariant().Contains(q))
            .ToList();
        Rows = list.Select(t => new TeacherListRow(t, new AsyncRelayCommand(() => nav.NavigateAsync<TeacherDetailViewModel>(t.Id)))).ToList();
        CountLabel = $"{list.Count} enseignant{(list.Count > 1 ? "s" : "")} affiché{(list.Count > 1 ? "s" : "")} sur {_all.Count}";
        HasNoData = _all.Count == 0;
        HasNoResults = _all.Count > 0 && list.Count == 0;
    }

    /// <summary>Clears the search and the active / inactive filter.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = "";
        SelectedFilter = "Tous";
    }

    [RelayCommand]
    private async Task Add()
    {
        var dialog = services.GetRequiredService<TeacherEditorDialogViewModel>();
        await dialog.InitializeAsync(null);
        if (await dialogs.ShowAsync(dialog) && dialog.SavedId is { } id)
        {
            notifier.Info("Enseignant ajouté");
            await nav.NavigateAsync<TeacherDetailViewModel>(id);
        }
    }

    [RelayCommand]
    private async Task Export()
    {
        var path = files.SaveFile("Exporter les enseignants", $"enseignants-{clock.GetLocalNow():yyyy-MM-dd}.xlsx", "Classeur Excel|*.xlsx");
        if (path is null) return;
        await RunAsync(async () =>
        {
            await export.ExportTableAsync(path, "Enseignants",
                ["Nom", "Téléphone", "Matière", "Groupes", "Élèves", "Rémunération", $"Gains {MonthLabel}", "Paiement", "Statut"],
                Rows.Select(r => (IReadOnlyList<object?>)[r.Name, r.Item.Phone, r.Subject, r.Groups, r.Students, r.Rule, r.Item.MonthEarnings, r.PayState?.Text, r.Status.Text]));
            notifier.Info("Export Excel généré");
            shell.Reveal(path);
        }, notifier);
    }

    // Ctrl+N in the shell.
    IAsyncRelayCommand? IHasPrimaryAction.PrimaryCommand => AddCommand;
    string? IHasPrimaryAction.PrimaryLabel => "Ajouter un enseignant";
}
