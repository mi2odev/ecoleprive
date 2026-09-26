using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Presentation.ViewModels.Pages;

/// <summary>Entry of the left list: one session of the selected day.</summary>
public sealed partial class AttendanceSessionItem(int id, int groupId, string time, string name, IRelayCommand select) : ObservableObject
{
    public int Id => id;
    public int GroupId => groupId;
    public string Time => time;
    public string Name => name;
    public IRelayCommand Select => select;

    [ObservableProperty] private string _progress = "";
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private bool _isSelected;
}

/// <summary>One of the four toggle chips of a student row.</summary>
public sealed partial class AttendanceChoice(AttendanceStatus status, BadgeKind kind, IRelayCommand select) : ObservableObject
{
    public AttendanceStatus Status => status;
    public string Label => Labels.Of(status);
    public BadgeKind Kind => kind;
    public IRelayCommand Select => select;

    [ObservableProperty] private bool _isOn;
}

public sealed partial class AttendanceStudentRow : ObservableObject
{
    private readonly Action _changed;

    public AttendanceStudentRow(AttendanceLine line, Action changed)
    {
        _changed = changed;
        StudentId = line.StudentId;
        Name = line.FullName;
        Matricule = line.Matricule;
        Choices = new[] { AttendanceStatus.Present, AttendanceStatus.Absent, AttendanceStatus.Late, AttendanceStatus.Excused }
            .Select(s => new AttendanceChoice(s, Badge.For(s).Kind, new RelayCommand(() => Status = s))).ToList();
        _status = line.Status;
        Sync();
    }

    public int StudentId { get; }
    public string Name { get; }
    public string Matricule { get; }
    public IReadOnlyList<AttendanceChoice> Choices { get; }

    [ObservableProperty] private AttendanceStatus? _status;

    partial void OnStatusChanged(AttendanceStatus? value)
    {
        Sync();
        _changed();
    }

    private void Sync()
    {
        foreach (var c in Choices) c.IsOn = c.Status == Status;
    }
}

public sealed record AttendanceCount(string Label, int Value);

public sealed partial class AttendanceViewModel(
    ISessionService sessions, IAttendanceService attendance, INotifier notifier, TimeProvider clock) : PageViewModel
{
    /// <summary>Navigation parameter: open the attendance sheet of a group on a date (or a specific session).</summary>
    public sealed record Target(DateTime Date, int? GroupId = null, int? SessionId = null);

    private bool _loading;

    public override string NavKey => "attendance";
    public override string Title => "Présences";

    [ObservableProperty] private DateTime? _day;
    [ObservableProperty] private string _dayLabel = "";
    [ObservableProperty] private IReadOnlyList<AttendanceSessionItem> _sessionItems = [];
    [ObservableProperty] private bool _hasNoSession;
    [ObservableProperty] private bool _hasSheet;
    [ObservableProperty] private AttendanceSessionItem? _selected;
    [ObservableProperty] private string _sessionName = "";
    [ObservableProperty] private string _sessionDetails = "";
    [ObservableProperty] private IReadOnlyList<AttendanceStudentRow> _rows = [];
    [ObservableProperty] private bool _hasNoStudent;
    [ObservableProperty] private IReadOnlyList<AttendanceCount> _counts = [];

    async partial void OnDayChanged(DateTime? value)
    {
        if (_loading || value is null) return;
        await LoadDayAsync(null, null);
    }

    public override async Task LoadAsync(object? parameter)
    {
        var target = parameter as Target;
        _loading = true;
        Day = (target?.Date ?? clock.GetLocalNow().DateTime).Date;
        _loading = false;
        await LoadDayAsync(target?.SessionId, target?.GroupId);
    }

    private async Task LoadDayAsync(int? sessionId, int? groupId)
    {
        var day = (Day ?? clock.GetLocalNow().DateTime).Date;
        await RunAsync(async () =>
        {
            DayLabel = Labels.LongDate(day);
            // Make sure the timetable's sessions exist for the day (existing ones are kept).
            await sessions.GenerateAsync(day, day);
            var list = (await sessions.ListAsync(day, day)).Where(s => s.Status != SessionStatus.Cancelled || s.Id == sessionId).ToList();
            SessionItems = list.Select(ToItem).ToList();
            HasNoSession = SessionItems.Count == 0;
            var pick = SessionItems.FirstOrDefault(i => i.Id == sessionId)
                ?? SessionItems.FirstOrDefault(i => groupId is not null && i.GroupId == groupId)
                ?? SessionItems.FirstOrDefault();
            await SelectAsync(pick);
        }, notifier);
    }

    private AttendanceSessionItem ToItem(Session s)
    {
        AttendanceSessionItem? item = null;
        item = new AttendanceSessionItem(s.Id, s.GroupId, $"{Labels.Time(s.Start)}–{Labels.Time(s.End)}", s.Group!.FullName,
            new AsyncRelayCommand(() => RunAsync(() => SelectAsync(item), notifier)));
        UpdateProgress(item, s.Attendance.Count, s.Group.Enrollments.Count(e => e.IsActiveOn(s.Date)));
        return item;
    }

    private static void UpdateProgress(AttendanceSessionItem item, int marked, int enrolled)
    {
        item.IsDone = marked > 0;
        item.Progress = marked > 0 ? $"{marked} / {Math.Max(marked, enrolled)} saisis" : "À faire";
    }

    private async Task SelectAsync(AttendanceSessionItem? item)
    {
        foreach (var i in SessionItems) i.IsSelected = i == item;
        Selected = item;
        HasSheet = item is not null;
        if (item is null)
        {
            Rows = [];
            Counts = [];
            SessionName = SessionDetails = "";
            HasNoStudent = false;
            return;
        }
        var sheet = await attendance.GetSheetAsync(item.Id);
        var s = sheet.Session;
        SessionName = s.Group!.FullName;
        SessionDetails = string.Join(" · ", new[]
        {
            $"{Labels.Time(s.Start)}–{Labels.Time(s.End)}",
            s.Room?.Name ?? s.Group.Room?.Name ?? "Sans salle",
            s.Teacher?.FullName ?? s.Group.Teacher?.FullName ?? "Sans enseignant",
        });
        Rows = sheet.Lines.Select(l => new AttendanceStudentRow(l, UpdateCounts)).ToList();
        HasNoStudent = Rows.Count == 0;
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        int N(AttendanceStatus s) => Rows.Count(r => r.Status == s);
        var counts = new List<AttendanceCount>
        {
            new("Présents", N(AttendanceStatus.Present)),
            new("Absents", N(AttendanceStatus.Absent)),
            new("Retards", N(AttendanceStatus.Late)),
        };
        if (N(AttendanceStatus.Excused) > 0) counts.Add(new("Excusés", N(AttendanceStatus.Excused)));
        counts.Add(new("Non saisis", Rows.Count(r => r.Status is null)));
        Counts = counts;
    }

    [RelayCommand]
    private void AllPresent()
    {
        foreach (var r in Rows) r.Status = AttendanceStatus.Present;
    }

    [RelayCommand]
    private void PreviousDay() => Day = (Day ?? clock.GetLocalNow().DateTime).Date.AddDays(-1);

    [RelayCommand]
    private void NextDay() => Day = (Day ?? clock.GetLocalNow().DateTime).Date.AddDays(1);

    [RelayCommand]
    private void Today() => Day = clock.GetLocalNow().DateTime.Date;

    [RelayCommand]
    private async Task Save()
    {
        if (Selected is not { } item) return;
        var marks = Rows.Where(r => r.Status is not null).ToDictionary(r => r.StudentId, r => r.Status!.Value);
        if (marks.Count == 0)
        {
            notifier.Info("Aucune présence à enregistrer");
            return;
        }
        if (await RunAsync(() => attendance.SaveAsync(item.Id, marks), notifier))
        {
            notifier.Info($"Présences enregistrées · {SessionName}");
            UpdateProgress(item, marks.Count, Rows.Count);
        }
    }
}
