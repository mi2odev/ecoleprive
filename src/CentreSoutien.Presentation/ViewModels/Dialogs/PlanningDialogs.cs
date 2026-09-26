using System.Globalization;
using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CentreSoutien.Presentation.ViewModels.Dialogs;

/// <summary>Create or edit a session (a dated class meeting of a group).</summary>
public sealed partial class SessionEditorDialogViewModel(
    ISessionService sessions, IGroupService groups, ICrudService<Room> rooms, ITeacherService teachers, TimeProvider clock) : DialogViewModel
{
    private Session _session = new();
    private List<Group> _groups = [];
    private bool _initializing;

    public override string Title => _session.Id == 0 ? "Nouvelle séance" : "Modifier la séance";
    public override double Width => 560;
    public int? SavedId { get; private set; }

    public IReadOnlyList<Option<SessionStatus>> Statuses => Options.SessionStatuses;

    [ObservableProperty] private IReadOnlyList<Option<int>> _groupOptions = [];
    [ObservableProperty] private Option<int>? _selectedGroup;
    [ObservableProperty] private DateTime? _date;
    [ObservableProperty] private string _start = "";
    [ObservableProperty] private string _end = "";
    [ObservableProperty] private IReadOnlyList<Option<int?>> _roomOptions = [];
    [ObservableProperty] private Option<int?>? _selectedRoom;
    [ObservableProperty] private IReadOnlyList<Option<int?>> _teacherOptions = [];
    [ObservableProperty] private Option<int?>? _selectedTeacher;
    [ObservableProperty] private Option<SessionStatus>? _selectedStatus;
    [ObservableProperty] private string? _topic;

    /// <summary>Loads the form for <paramref name="existing"/> or a new session on <paramref name="date"/> (optionally for a group).</summary>
    public async Task InitializeAsync(Session? existing, DateTime? date = null, int? groupId = null)
    {
        _initializing = true;
        try
        {
            var now = clock.GetLocalNow().DateTime;
            _session = existing ?? new Session { Date = (date ?? now).Date, GroupId = groupId ?? 0, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(11, 0, 0) };
            OnPropertyChanged(nameof(Title));
            _groups = (await groups.ListAsync()).Where(g => (g.IsActive && g.Course!.IsActive) || g.Id == _session.GroupId).ToList();
            GroupOptions = _groups.Select(g => new Option<int>(g.Id, g.FullName)).ToList();
            RoomOptions = [new Option<int?>(null, "Aucune salle"), .. (await rooms.ListAsync()).Where(r => r.IsActive || r.Id == _session.RoomId)
                .OrderBy(r => r.Name).Select(r => new Option<int?>(r.Id, r.Name))];
            TeacherOptions = [new Option<int?>(null, "Aucun enseignant"), .. (await teachers.ListAsync(now)).Where(t => t.IsActive || t.Id == _session.TeacherId)
                .OrderBy(t => t.FullName).Select(t => new Option<int?>(t.Id, t.FullName))];

            SelectedGroup = GroupOptions.FirstOrDefault(g => g.Value == _session.GroupId);
            Date = _session.Date;
            Start = Parse.Time(_session.Start);
            End = Parse.Time(_session.End);
            SelectedStatus = Statuses.First(s => s.Value == _session.Status);
            Topic = _session.Topic;
            if (existing is null && SelectedGroup is not null) ApplyGroupDefaults();
            else
            {
                SelectedRoom = RoomOptions.FirstOrDefault(r => r.Value == _session.RoomId) ?? RoomOptions[0];
                SelectedTeacher = TeacherOptions.FirstOrDefault(t => t.Value == _session.TeacherId) ?? TeacherOptions[0];
            }
        }
        finally
        {
            _initializing = false;
        }
    }

    partial void OnSelectedGroupChanged(Option<int>? value)
    {
        if (!_initializing) ApplyGroupDefaults();
    }

    /// <summary>Room and teacher default to the group's; times follow the group's timetable slot on that weekday.</summary>
    private void ApplyGroupDefaults()
    {
        var g = _groups.FirstOrDefault(x => x.Id == SelectedGroup?.Value);
        if (g is null) return;
        SelectedRoom = RoomOptions.FirstOrDefault(r => r.Value == g.RoomId) ?? RoomOptions[0];
        SelectedTeacher = TeacherOptions.FirstOrDefault(t => t.Value == g.TeacherId) ?? TeacherOptions[0];
        if (Date is { } d && g.Slots.FirstOrDefault(s => s.Day == d.DayOfWeek) is { } slot)
        {
            Start = Parse.Time(slot.Start);
            End = Parse.Time(slot.End);
        }
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        if (SelectedGroup is null) throw new BusinessException("Choisissez un groupe.");
        if (Date is null) throw new BusinessException("Choisissez une date.");
        var start = Parse.Time(Start) ?? throw new BusinessException("Heure de début invalide (ex. 09:00).");
        var end = Parse.Time(End) ?? throw new BusinessException("Heure de fin invalide (ex. 11:00).");
        var s = new Session
        {
            Id = _session.Id,
            GroupId = SelectedGroup.Value,
            Date = Date.Value.Date,
            Start = start,
            End = end,
            RoomId = SelectedRoom?.Value,
            TeacherId = SelectedTeacher?.Value,
            Status = SelectedStatus?.Value ?? SessionStatus.Planned,
            Topic = string.IsNullOrWhiteSpace(Topic) ? null : Topic.Trim(),
        };
        var saved = await sessions.SaveAsync(s);
        SavedId = saved.Id;
        return true;
    }
}

/// <summary>Create or edit an evaluation (test, homework, exam) of a group.</summary>
public sealed partial class ExamEditorDialogViewModel(IExamService exams, IGroupService groups, TimeProvider clock) : DialogViewModel
{
    private Exam _exam = new();

    public override string Title => _exam.Id == 0 ? "Nouvel examen" : "Modifier l'examen";
    public override double Width => 560;
    public int? SavedId { get; private set; }

    public IReadOnlyList<Option<ExamType>> Types => Options.ExamTypes;

    [ObservableProperty] private IReadOnlyList<Option<int>> _groupOptions = [];
    [ObservableProperty] private Option<int>? _selectedGroup;
    [ObservableProperty] private string _examTitle = "";
    [ObservableProperty] private Option<ExamType>? _selectedType;
    [ObservableProperty] private DateTime? _date;
    [ObservableProperty] private string _maxScore = "20";
    [ObservableProperty] private string _coefficient = "1";
    [ObservableProperty] private string? _notes;

    public async Task InitializeAsync(Exam? existing, int? groupId = null)
    {
        _exam = existing ?? new Exam { Date = clock.GetLocalNow().Date, GroupId = groupId ?? 0 };
        OnPropertyChanged(nameof(Title));
        GroupOptions = (await groups.ListAsync()).Where(g => g.IsActive || g.Id == _exam.GroupId).Select(g => new Option<int>(g.Id, g.FullName)).ToList();
        SelectedGroup = GroupOptions.FirstOrDefault(g => g.Value == _exam.GroupId);
        ExamTitle = _exam.Title;
        SelectedType = Types.First(t => t.Value == _exam.Type);
        Date = _exam.Date;
        MaxScore = Number(_exam.MaxScore);
        Coefficient = Number(_exam.Coefficient);
        Notes = _exam.Notes;
    }

    private static string Number(decimal v) => v.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR"));

    /// <summary>Parses a score or coefficient typed with a comma or a dot ("14,5", "0.5").</summary>
    public static decimal? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().Replace(" ", "").Replace(',', '.');
        return decimal.TryParse(t, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    protected override async Task<bool> OnConfirmAsync()
    {
        if (SelectedGroup is null) throw new BusinessException("Choisissez un groupe.");
        if (string.IsNullOrWhiteSpace(ExamTitle)) throw new BusinessException("L'intitulé est obligatoire.");
        var max = ParseNumber(MaxScore) ?? throw new BusinessException("Note maximale invalide.");
        var coef = ParseNumber(Coefficient) ?? throw new BusinessException("Coefficient invalide.");
        var e = new Exam
        {
            Id = _exam.Id,
            GroupId = SelectedGroup.Value,
            Title = ExamTitle.Trim(),
            Type = SelectedType?.Value ?? ExamType.Test,
            Date = (Date ?? clock.GetLocalNow().Date).Date,
            MaxScore = max,
            Coefficient = coef,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
        };
        var saved = await exams.SaveAsync(e);
        SavedId = saved.Id;
        return true;
    }
}
