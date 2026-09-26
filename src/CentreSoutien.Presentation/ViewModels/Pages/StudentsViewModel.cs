using System.Collections.ObjectModel;
using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Presentation.ViewModels.Pages;

public sealed record StudentRow(StudentListItem Item, IRelayCommand Open)
{
    public int Id => Item.Id;
    public string Matricule => Item.Matricule;
    public string Name => Item.FullName;
    public string Initials => Item.Initials;
    public string Level => Item.Level;
    public string Courses => Item.Courses;
    public string Parent => Item.ParentPhone ?? "—";
    public decimal BalanceValue => Item.Balance;
    public string Balance => Item.Balance > 0 ? Money.Format(Item.Balance) : "—";
    public Badge State => Badge.For(Item.State);
}

public sealed partial class StudentsViewModel(
    IStudentService students, INavigator nav, DialogHost dialogs, INotifier notifier, IFilePicker files, IExportService export,
    IShell shell, TimeProvider clock, IServiceProvider services) : PageViewModel
{
    /// <summary>Navigation parameter: pre-filled search (from the header search box).</summary>
    public sealed record Query(string Text);

    private List<StudentListItem> _all = [];

    public override string NavKey => "students";
    public override string Title => "Élèves";

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private IReadOnlyList<string> _levels = ["Tous"];
    [ObservableProperty] private string _selectedLevel = "Tous";
    [ObservableProperty] private IReadOnlyList<string> _states = ["Tous", "Payé", "Partiel", "Impayé"];
    [ObservableProperty] private string _selectedState = "Tous";
    [ObservableProperty] private IReadOnlyList<StudentRow> _rows = [];
    [ObservableProperty] private string _countLabel = "";
    [ObservableProperty] private string _monthLabel = "";
    /// <summary>Nothing recorded yet: the page shows its "getting started" empty state.</summary>
    [ObservableProperty] private bool _hasNoData;
    /// <summary>There is data, but the current search / filters hide all of it.</summary>
    [ObservableProperty] private bool _hasNoResults;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedLevelChanged(string value) => ApplyFilter();
    partial void OnSelectedStateChanged(string value) => ApplyFilter();

    public override async Task LoadAsync(object? parameter)
    {
        if (parameter is Query q) SearchText = q.Text;
        await RunAsync(async () =>
        {
            var now = clock.GetLocalNow().DateTime;
            MonthLabel = Labels.Months[now.Month - 1];
            _all = await students.ListAsync(now);
            var levels = _all.Select(s => s.Level).Where(l => !string.IsNullOrEmpty(l)).Distinct()
                .OrderBy(Domain.Calculations.Levels.Order).ToList();
            Levels = ["Tous", .. levels, "Nouveaux", "Inactifs"];
            if (!Levels.Contains(SelectedLevel)) SelectedLevel = "Tous";
            ApplyFilter();
        }, notifier);
    }

    private void ApplyFilter()
    {
        var q = SearchText.Trim().ToLowerInvariant();
        var list = _all.Where(s => SelectedLevel switch
            {
                "Tous" => true,
                "Inactifs" => !s.IsActive,
                "Nouveaux" => s.IsNew,
                _ => s.IsActive && s.Level == SelectedLevel,
            })
            .Where(s => SelectedState == "Tous" || Labels.Of(s.State) == SelectedState)
            .Where(s => q.Length == 0 || $"{s.FullName} {s.Matricule} {s.ParentPhone} {s.ParentName}".ToLowerInvariant().Contains(q))
            .ToList();
        Rows = list.Select(s => new StudentRow(s, new AsyncRelayCommand(() => nav.NavigateAsync<StudentDetailViewModel>(s.Id)))).ToList();
        CountLabel = $"{list.Count} élève{(list.Count > 1 ? "s" : "")} affiché{(list.Count > 1 ? "s" : "")} sur {_all.Count}";
        HasNoData = _all.Count == 0;
        HasNoResults = _all.Count > 0 && list.Count == 0;
    }

    /// <summary>Clears the search and the level / payment filters.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = "";
        SelectedLevel = "Tous";
        SelectedState = "Tous";
    }

    [RelayCommand]
    private async Task Add()
    {
        var dialog = services.GetRequiredService<StudentEditorDialogViewModel>();
        await dialog.InitializeAsync(null);
        if (await dialogs.ShowAsync(dialog) && dialog.SavedId is { } id)
        {
            notifier.Info("Élève ajouté");
            await nav.NavigateAsync<StudentDetailViewModel>(id);
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        var dialog = services.GetRequiredService<ImportStudentsDialogViewModel>();
        if (!await dialogs.ShowAsync(dialog) || dialog.Result is not { } r) return;
        var summary = $"{r.StudentsCreated} élève{(r.StudentsCreated > 1 ? "s" : "")} importé{(r.StudentsCreated > 1 ? "s" : "")}";
        if (r.ParentsCreated > 0) summary += $" · {r.ParentsCreated} parent{(r.ParentsCreated > 1 ? "s" : "")} créé{(r.ParentsCreated > 1 ? "s" : "")}";
        if (r.Enrollments > 0) summary += $" · {r.Enrollments} inscription{(r.Enrollments > 1 ? "s" : "")}";
        if (r.Skipped > 0) summary += $" · {r.Skipped} ligne{(r.Skipped > 1 ? "s" : "")} ignorée{(r.Skipped > 1 ? "s" : "")}";
        notifier.Info(summary);
        await LoadAsync(null);
    }

    [RelayCommand]
    private async Task Export()
    {
        var path = files.SaveFile("Exporter les élèves", $"eleves-{clock.GetLocalNow():yyyy-MM-dd}.xlsx", "Classeur Excel|*.xlsx");
        if (path is null) return;
        await RunAsync(async () =>
        {
            await export.ExportTableAsync(path, "Élèves",
                ["Matricule", "Nom", "Niveau", "Cours", "Parent", "Téléphone parent", "Mensualité", "Reste à payer", "Statut"],
                Rows.Select(r => (IReadOnlyList<object?>)[r.Matricule, r.Name, r.Level, r.Courses, r.Item.ParentName, r.Item.ParentPhone, r.Item.MonthlyDue, r.Item.Balance, r.State.Text]));
            notifier.Info("Export Excel généré");
            shell.Reveal(path);
        }, notifier);
    }
}
