using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Presentation.ViewModels.Pages;

public sealed record SubjectRow(int Id, string Name, string ShortName, string? Description, int CourseCount, int TeacherCount, IRelayCommand Edit, IRelayCommand Delete)
{
    public string Courses => CourseCount == 0 ? "—" : $"{CourseCount} cours";
    public string Teachers => TeacherCount == 0 ? "—" : $"{TeacherCount} enseignant{(TeacherCount > 1 ? "s" : "")}";
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}

public sealed partial class SubjectsViewModel(
    ICrudService<Subject> subjects, ICourseService courses, ITeacherService teachers, DialogHost dialogs, INotifier notifier,
    TimeProvider clock, IServiceProvider services) : PageViewModel
{
    private List<SubjectRow> _all = [];

    public override string NavKey => "subjects";
    public override string Title => "Matières";

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private IReadOnlyList<SubjectRow> _rows = [];
    [ObservableProperty] private string _countLabel = "";
    /// <summary>Nothing recorded yet: the page shows its "getting started" empty state.</summary>
    [ObservableProperty] private bool _hasNoData;
    /// <summary>There is data, but the current search / filters hide all of it.</summary>
    [ObservableProperty] private bool _hasNoResults;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public override async Task LoadAsync(object? parameter)
    {
        await RunAsync(async () =>
        {
            var list = await subjects.ListAsync();
            var courseList = await courses.ListAsync();
            var teacherList = await teachers.ListAsync(clock.GetLocalNow().DateTime);
            _all = list.Select(s =>
            {
                var cs = courseList.Where(c => c.SubjectId == s.Id).ToList();
                // Teachers of the subject: by speciality or by teaching one of its groups.
                var teacherIds = teacherList.Where(t => t.Subject == s.Name).Select(t => t.Id)
                    .Union(cs.SelectMany(c => c.Groups).Where(g => g.TeacherId is not null).Select(g => g.TeacherId!.Value))
                    .Distinct().Count();
                return new SubjectRow(s.Id, s.Name, string.IsNullOrWhiteSpace(s.ShortName) ? "—" : s.ShortName, s.Description, cs.Count, teacherIds,
                    new AsyncRelayCommand(() => EditAsync(s.Id)),
                    new AsyncRelayCommand(() => DeleteAsync(s)));
            }).ToList();
            ApplyFilter();
        }, notifier);
    }

    private void ApplyFilter()
    {
        var q = SearchText.Trim().ToLowerInvariant();
        Rows = _all.Where(s => q.Length == 0 || $"{s.Name} {s.ShortName}".ToLowerInvariant().Contains(q)).ToList();
        CountLabel = $"{_all.Count} matière{(_all.Count > 1 ? "s" : "")}";
        HasNoData = _all.Count == 0;
        HasNoResults = _all.Count > 0 && Rows.Count == 0;
    }

    /// <summary>Clears the search.</summary>
    [RelayCommand]
    private void ClearFilters() => SearchText = "";

    [RelayCommand]
    private Task Add() => EditAsync(null);

    private async Task EditAsync(int? id)
    {
        var dialog = services.GetRequiredService<SubjectEditorDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(id), notifier)) return;
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info(id is null ? "Matière ajoutée" : "Matière enregistrée");
            await RefreshAsync();
        }
    }

    private async Task DeleteAsync(Subject s)
    {
        if (!await dialogs.ConfirmAsync("Supprimer la matière", $"Supprimer « {s.Name} » ?")) return;
        if (await RunAsync(() => subjects.DeleteAsync(s.Id), notifier))
        {
            notifier.Info("Matière supprimée");
            await RefreshAsync();
        }
    }
}
