using System.Globalization;
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

/// <summary>Editable line of the grade sheet.</summary>
public sealed partial class GradeEntryRow(int studentId, string name, string matricule) : ObservableObject
{
    public int StudentId => studentId;
    public string Name => name;
    public string Matricule => matricule;

    [ObservableProperty] private string _score = "";
    [ObservableProperty] private string? _comment;
    [ObservableProperty] private bool _isInvalid;

    partial void OnScoreChanged(string value) => IsInvalid = false;
}

public sealed record StudentAverageRow(string Name, string Matricule, string Average, bool Passing);

public sealed partial class GradesViewModel(
    IExamService exams, IGroupService groups, ISettingsService settings, DialogHost dialogs, INotifier notifier,
    TimeProvider clock, IServiceProvider services) : PageViewModel
{
    /// <summary>Navigation parameter: a group and optionally the evaluation to open. A plain <c>int</c> group id is also accepted.</summary>
    public sealed record Target(int GroupId, int? ExamId = null);

    private CenterSettings _settings = new();
    private List<Exam> _groupExams = [];
    private Exam? _exam;
    private bool _loading;

    public override string NavKey => "grades";
    public override string Title => "Notes";

    [ObservableProperty] private IReadOnlyList<Option<int>> _groupOptions = [];
    [ObservableProperty] private Option<int>? _selectedGroup;
    [ObservableProperty] private IReadOnlyList<Option<int>> _examOptions = [];
    [ObservableProperty] private Option<int>? _selectedExam;
    [ObservableProperty] private bool _hasNoExam;
    [ObservableProperty] private bool _hasExam;
    [ObservableProperty] private string _examTitle = "";
    [ObservableProperty] private string _examDetails = "";
    [ObservableProperty] private string _scoreHeader = "Note";
    [ObservableProperty] private IReadOnlyList<GradeEntryRow> _rows = [];
    [ObservableProperty] private bool _hasNoStudent;
    [ObservableProperty] private IReadOnlyList<Field> _summary = [];
    [ObservableProperty] private string _scaleLabel = "";
    [ObservableProperty] private IReadOnlyList<StudentAverageRow> _groupAverages = [];
    [ObservableProperty] private bool _hasNoAverage;

    async partial void OnSelectedGroupChanged(Option<int>? value)
    {
        if (_loading || value is null) return;
        await RunAsync(() => LoadGroupAsync(value.Value, null), notifier);
    }

    async partial void OnSelectedExamChanged(Option<int>? value)
    {
        if (_loading) return;
        await RunAsync(() => LoadSheetAsync(value?.Value), notifier);
    }

    public override async Task LoadAsync(object? parameter)
    {
        var (groupId, examId) = parameter switch
        {
            Target t => ((int?)t.GroupId, t.ExamId),
            int id => (id, (int?)null),
            _ => (null, null),
        };
        await RunAsync(async () =>
        {
            _settings = await settings.GetAsync();
            ScaleLabel = $"/{_settings.GradeScale:0.##}";
            var list = await groups.ListAsync();
            _loading = true;
            try
            {
                GroupOptions = list.Where(g => g.IsActive || g.Id == groupId).Select(g => new Option<int>(g.Id, g.FullName)).ToList();
                var keep = groupId ?? SelectedGroup?.Value;
                SelectedGroup = GroupOptions.FirstOrDefault(g => g.Value == keep) ?? GroupOptions.FirstOrDefault();
            }
            finally
            {
                _loading = false;
            }
            if (SelectedGroup is not null) await LoadGroupAsync(SelectedGroup.Value, examId ?? (groupId is null ? SelectedExam?.Value : null));
        }, notifier);
    }

    private async Task LoadGroupAsync(int groupId, int? examId)
    {
        _groupExams = (await exams.ListAsync()).Where(e => e.GroupId == groupId).OrderBy(e => e.Date).ThenBy(e => e.Title).ToList();
        foreach (var e in _groupExams)
        foreach (var g in e.Grades)
            g.Exam ??= e;
        _loading = true;
        try
        {
            ExamOptions = _groupExams.Select(e => new Option<int>(e.Id, e.Title)).ToList();
            SelectedExam = ExamOptions.FirstOrDefault(o => o.Value == examId) ?? ExamOptions.LastOrDefault();
        }
        finally
        {
            _loading = false;
        }
        HasNoExam = ExamOptions.Count == 0;
        await LoadAveragesAsync(groupId);
        await LoadSheetAsync(SelectedExam?.Value);
    }

    private async Task LoadAveragesAsync(int groupId)
    {
        var group = await groups.GetAsync(groupId);
        var today = clock.GetLocalNow().Date;
        var students = group?.Enrollments.Where(e => e.IsActiveOn(today)).Select(e => e.Student!)
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToList() ?? [];
        GroupAverages = students.Select(s =>
        {
            var avg = Grading.Average(_groupExams.SelectMany(e => e.Grades).Where(g => g.StudentId == s.Id), _settings.GradeScale, _settings.GradeDecimals);
            return new StudentAverageRow(s.FullName, s.Matricule, avg is null ? "—" : avg.Value.ToString("0.00"), avg is null || avg >= _settings.PassingGrade);
        }).ToList();
        HasNoAverage = GroupAverages.Count == 0;
    }

    private async Task LoadSheetAsync(int? examId)
    {
        if (examId is null)
        {
            _exam = null;
            HasExam = false;
            Rows = [];
            Summary = [];
            HasNoStudent = false;
            return;
        }
        var sheet = await exams.GetGradeSheetAsync(examId.Value);
        _exam = sheet.Exam;
        HasExam = true;
        ExamTitle = _exam.Title;
        ExamDetails = $"{Labels.Of(_exam.Type)} · {_exam.Date:dd/MM/yyyy} · noté sur {_exam.MaxScore:0.##} · coefficient {_exam.Coefficient:0.##}";
        ScoreHeader = $"Note /{_exam.MaxScore:0.##}";
        Rows = sheet.Lines.Select(l => new GradeEntryRow(l.StudentId, l.FullName, l.Matricule)
        {
            Score = l.Score is null ? "" : l.Score.Value.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR")),
            Comment = l.Comment,
        }).ToList();
        HasNoStudent = Rows.Count == 0;
        UpdateSummary(sheet.Lines.Select(l => l.Score));
    }

    private void UpdateSummary(IEnumerable<decimal?> scores)
    {
        if (_exam is null) return;
        var scale = _settings.GradeScale;
        var normalised = scores.Where(s => s is not null).Select(s => s!.Value / _exam.MaxScore * scale).ToList();
        string F(decimal? v) => v is null ? "—" : Math.Round(v.Value, _settings.GradeDecimals).ToString("0.00");
        Summary =
        [
            new("Moyenne de la classe", F(normalised.Count == 0 ? null : normalised.Average())),
            new("Note la plus basse", F(normalised.Count == 0 ? null : normalised.Min())),
            new("Note la plus haute", F(normalised.Count == 0 ? null : normalised.Max())),
            new($"Au moins {_settings.PassingGrade:0.##}/{scale:0.##}", $"{normalised.Count(v => v >= _settings.PassingGrade)} / {normalised.Count}"),
        ];
    }

    [RelayCommand]
    private async Task Save()
    {
        if (_exam is not { } exam) return;
        var scores = new Dictionary<int, decimal?>();
        var comments = new Dictionary<int, string?>();
        var invalid = new List<GradeEntryRow>();
        foreach (var r in Rows)
        {
            decimal? score = null;
            if (!string.IsNullOrWhiteSpace(r.Score))
            {
                score = ExamEditorDialogViewModel.ParseNumber(r.Score);
                if (score is null || score < 0 || score > exam.MaxScore)
                {
                    r.IsInvalid = true;
                    invalid.Add(r);
                    continue;
                }
            }
            scores[r.StudentId] = score;
            comments[r.StudentId] = string.IsNullOrWhiteSpace(r.Comment) ? null : r.Comment.Trim();
        }
        if (invalid.Count > 0)
        {
            var names = string.Join(", ", invalid.Take(3).Select(r => r.Name)) + (invalid.Count > 3 ? "…" : "");
            Error = $"Note invalide pour {names} : saisissez une valeur entre 0 et {exam.MaxScore:0.##}.";
            notifier.Error(Error);
            return;
        }
        if (await RunAsync(() => exams.SaveGradesAsync(exam.Id, scores, comments), notifier))
        {
            notifier.Info($"Notes enregistrées · {exam.Title}");
            await RunAsync(() => LoadGroupAsync(exam.GroupId, exam.Id), notifier);
        }
    }

    [RelayCommand]
    private async Task NewExam()
    {
        var dialog = services.GetRequiredService<ExamEditorDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(null, SelectedGroup?.Value), notifier)) return;
        if (await dialogs.ShowAsync(dialog) && dialog.SavedId is { } id)
        {
            notifier.Info("Examen créé");
            var groupId = SelectedGroup?.Value;
            await RunAsync(async () =>
            {
                // The exam may have been created for another group: follow it.
                var exam = (await exams.ListAsync()).FirstOrDefault(e => e.Id == id);
                if (exam is not null && exam.GroupId != groupId)
                {
                    _loading = true;
                    SelectedGroup = GroupOptions.FirstOrDefault(g => g.Value == exam.GroupId) ?? SelectedGroup;
                    _loading = false;
                }
                await LoadGroupAsync(SelectedGroup!.Value, id);
            }, notifier);
        }
    }
}
