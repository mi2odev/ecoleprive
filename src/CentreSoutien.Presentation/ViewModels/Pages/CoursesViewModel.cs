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

public sealed record CourseRow(Course Course, string Groups, string Teachers, string Schedule, int Enrolled, int Capacity, IRelayCommand Open)
{
    public int Id => Course.Id;
    public string Name => Course.Name;
    public string Level => Course.Level;
    public string Fill => $"{Enrolled} / {Capacity}";
    public bool IsFull => Capacity > 0 && Enrolled >= Capacity;
    public Badge FullBadge => new(Fill, BadgeKind.Warn);
    public string Price => Money.Format(Course.MonthlyPrice);
    public bool IsInactive => !Course.IsActive;
}

public sealed partial class CoursesViewModel(
    ICourseService courses, INavigator nav, DialogHost dialogs, INotifier notifier, TimeProvider clock, IServiceProvider services) : PageViewModel
{
    private List<Course> _all = [];

    public override string NavKey => "courses";
    public override string Title => "Cours";

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private IReadOnlyList<string> _levels = ["Tous"];
    [ObservableProperty] private string _selectedLevel = "Tous";
    [ObservableProperty] private IReadOnlyList<CourseRow> _rows = [];
    [ObservableProperty] private string _countLabel = "";

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedLevelChanged(string value) => ApplyFilter();

    public override async Task LoadAsync(object? parameter)
    {
        await RunAsync(async () =>
        {
            _all = await courses.ListAsync();
            Levels = ["Tous", .. _all.Select(c => c.Level).Where(l => !string.IsNullOrEmpty(l)).Distinct().OrderBy(Domain.Calculations.Levels.Order)];
            if (!Levels.Contains(SelectedLevel)) SelectedLevel = "Tous";
            ApplyFilter();
        }, notifier);
    }

    private void ApplyFilter()
    {
        var today = clock.GetLocalNow().Date;
        var q = SearchText.Trim().ToLowerInvariant();
        var list = _all
            .Where(c => SelectedLevel == "Tous" || c.Level == SelectedLevel)
            .Where(c => q.Length == 0 || $"{c.Name} {string.Join(" ", c.Groups.Select(g => g.Teacher?.FullName))}".ToLowerInvariant().Contains(q))
            .ToList();
        Rows = list.Select(c => ToRow(c, today, nav)).ToList();
        CountLabel = $"{list.Count} cours affiché{(list.Count > 1 ? "s" : "")} sur {_all.Count}";
    }

    public static CourseRow ToRow(Course c, DateTime today, INavigator nav)
    {
        var groups = c.Groups.Where(g => g.IsActive).OrderBy(g => g.Name).ToList();
        var teachers = groups.Select(g => g.Teacher?.FullName).Where(n => n is not null).Distinct().ToList();
        var schedule = groups.Count switch
        {
            0 => "Aucun groupe",
            1 => groups[0].Slots.Count == 0 ? "Non planifié" : Labels.Slots(groups[0].Slots),
            _ => $"{groups.Count} groupes",
        };
        var enrolled = groups.Sum(g => g.Enrollments.Count(e => e.IsActiveOn(today)));
        return new CourseRow(c, groups.Count == 0 ? "—" : string.Join(", ", groups.Select(g => g.Name)),
            teachers.Count == 0 ? "—" : string.Join(", ", teachers), schedule, enrolled, groups.Sum(g => g.Capacity),
            new AsyncRelayCommand(() => nav.NavigateAsync<CourseDetailViewModel>(new CourseDetailViewModel.Target(c.Id))));
    }

    [RelayCommand]
    private async Task Add()
    {
        var dialog = services.GetRequiredService<CourseEditorDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(null), notifier)) return;
        if (await dialogs.ShowAsync(dialog) && dialog.SavedId is { } id)
        {
            notifier.Info("Cours ajouté");
            await nav.NavigateAsync<CourseDetailViewModel>(new CourseDetailViewModel.Target(id));
        }
    }
}
