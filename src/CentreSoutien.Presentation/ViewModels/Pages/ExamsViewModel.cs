using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Presentation.ViewModels.Pages;

public sealed record ExamRow(
    int Id, int GroupId, string Date, string Title, string Type, string Group, string Max, string Coefficient, string Entered, string Average,
    bool Passing, IRelayCommand EnterGrades, IRelayCommand Edit, IRelayCommand Delete);

public sealed partial class ExamsViewModel(
    IExamService exams, IGroupService groups, ISettingsService settings, INavigator nav, DialogHost dialogs, INotifier notifier,
    IServiceProvider services) : PageViewModel, IHasPrimaryAction
{
    private List<Exam> _all = [];
    private Dictionary<int, Group> _groups = [];
    private CenterSettings _settings = new();
    private bool _loading;

    public override string NavKey => "exams";
    public override string Title => "Examens";

    public IReadOnlyList<Option<ExamType?>> TypeFilters { get; } =
        [new(null, "Tous"), .. Options.ExamTypes.Select(t => new Option<ExamType?>(t.Value, t.Label))];

    [ObservableProperty] private IReadOnlyList<Option<int?>> _groupOptions = [];
    [ObservableProperty] private Option<int?>? _selectedGroup;
    [ObservableProperty] private Option<ExamType?>? _selectedType;
    [ObservableProperty] private IReadOnlyList<ExamRow> _rows = [];
    [ObservableProperty] private string _countLabel = "";
    [ObservableProperty] private string _averageHeader = "Moyenne";

    partial void OnSelectedGroupChanged(Option<int?>? value) { if (!_loading) ApplyFilter(); }
    partial void OnSelectedTypeChanged(Option<ExamType?>? value) { if (!_loading) ApplyFilter(); }

    public override async Task LoadAsync(object? parameter)
    {
        var groupId = parameter as int?;
        await RunAsync(async () =>
        {
            _loading = true;
            try
            {
                _settings = await settings.GetAsync();
                AverageHeader = $"Moyenne /{_settings.GradeScale:0.##}";
                var groupList = await groups.ListAsync();
                _groups = groupList.ToDictionary(g => g.Id);
                _all = await exams.ListAsync();
                var previous = SelectedGroup is null ? groupId : SelectedGroup.Value;
                GroupOptions = [new Option<int?>(null, "Tous les groupes"), .. groupList.Where(g => g.IsActive || _all.Any(e => e.GroupId == g.Id))
                    .Select(g => new Option<int?>(g.Id, g.FullName))];
                SelectedGroup = GroupOptions.FirstOrDefault(g => g.Value == previous) ?? GroupOptions[0];
                SelectedType ??= TypeFilters[0];
            }
            finally
            {
                _loading = false;
            }
            ApplyFilter();
        }, notifier);
    }

    private void ApplyFilter()
    {
        var list = _all
            .Where(e => SelectedGroup?.Value is not { } g || e.GroupId == g)
            .Where(e => SelectedType?.Value is not { } t || e.Type == t)
            .ToList();
        Rows = list.Select(ToRow).ToList();
        CountLabel = $"{list.Count} évaluation{(list.Count > 1 ? "s" : "")}";
    }

    private ExamRow ToRow(Exam e)
    {
        foreach (var g in e.Grades) g.Exam ??= e;
        var enrolled = _groups.TryGetValue(e.GroupId, out var group) ? group.Enrollments.Count(x => x.IsActiveOn(e.Date)) : 0;
        var entered = e.Grades.Count(g => g.Score is not null);
        var avg = Grading.Average(e.Grades, _settings.GradeScale, _settings.GradeDecimals);
        return new ExamRow(e.Id, e.GroupId, e.Date.ToString("dd/MM/yyyy"), e.Title, Labels.Of(e.Type), e.Group?.FullName ?? "—",
            e.MaxScore.ToString("0.##"), e.Coefficient.ToString("0.##"), $"{entered} / {Math.Max(entered, enrolled)}",
            avg is null ? "—" : avg.Value.ToString("0.00"), avg is null || avg >= _settings.PassingGrade,
            new AsyncRelayCommand(() => nav.NavigateAsync<GradesViewModel>(new GradesViewModel.Target(e.GroupId, e.Id))),
            new AsyncRelayCommand(() => EditAsync(e)),
            new AsyncRelayCommand(() => DeleteAsync(e)));
    }

    [RelayCommand]
    private async Task Add()
    {
        var dialog = services.GetRequiredService<ExamEditorDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(null, SelectedGroup?.Value), notifier)) return;
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Examen créé");
            await RefreshAsync();
        }
    }

    private async Task EditAsync(Exam e)
    {
        var dialog = services.GetRequiredService<ExamEditorDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(e), notifier)) return;
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info("Examen enregistré");
            await RefreshAsync();
        }
    }

    private async Task DeleteAsync(Exam e)
    {
        if (!await dialogs.ConfirmAsync("Supprimer l'examen", $"Supprimer « {e.Title} » ({e.Group?.FullName}) et toutes ses notes ? Cette action est irréversible.")) return;
        if (await RunAsync(() => exams.DeleteAsync(e.Id), notifier))
        {
            notifier.Info("Examen supprimé");
            await RefreshAsync();
        }
    }

    // Ctrl+N in the shell.
    IAsyncRelayCommand? IHasPrimaryAction.PrimaryCommand => AddCommand;
    string? IHasPrimaryAction.PrimaryLabel => "Nouvel examen";
}
