using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Presentation.ViewModels.Pages;

/// <summary>Week helpers shared by the planning screens (the Algerian week starts on Saturday).</summary>
public static class PlanningWeek
{
    public static DateTime StartOf(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 1) % 7));

    /// <summary>"Semaine du 26 septembre au 2 octobre 2026".</summary>
    public static string Label(DateTime start, int days = 7)
    {
        var end = start.AddDays(days - 1);
        var from = start.Year != end.Year ? $"{Day(start)} {MonthName(start)} {start.Year}"
            : start.Month != end.Month ? $"{Day(start)} {MonthName(start)}"
            : Day(start);
        return $"Semaine du {from} au {Day(end)} {MonthName(end)} {end.Year}";
    }

    /// <summary>"Samedi 26 septembre".</summary>
    public static string DayTitle(DateTime d) => $"{Labels.Day(d.DayOfWeek)} {Day(d)} {MonthName(d)}";

    private static string Day(DateTime d) => d.Day == 1 ? "1er" : d.Day.ToString();
    private static string MonthName(DateTime d) => Labels.Months[d.Month - 1].ToLowerInvariant();
}

/// <summary>A timetable slot drawn on the weekly grid.</summary>
public sealed record ScheduleBlock(
    int SlotId, int GroupId, int CourseId, string Name, string Time, string Room, string Teacher,
    double Top, double Height, int Lane, bool IsLive, IRelayCommand Open)
{
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
}

/// <summary>Blocks of one lane of a day column (overlapping slots share the column side by side).</summary>
public sealed record ScheduleLane(IReadOnlyList<ScheduleBlock> Blocks);

public sealed record ScheduleDay(
    DayOfWeek Day, DateTime Date, string Label, bool IsToday, bool IsVisible, double Weight, int LaneCount,
    IReadOnlyList<ScheduleLane> Lanes, double NowTop, bool ShowNow)
{
    public IEnumerable<ScheduleBlock> Blocks => Lanes.SelectMany(l => l.Blocks);
}

public sealed record HourMark(string Label, double Top);

public sealed partial class ScheduleViewModel(
    IScheduleService schedule, INavigator nav, DialogHost dialogs, INotifier notifier, TimeProvider clock, IServiceProvider services) : PageViewModel
{
    public const int FirstHour = 8;
    public const int LastHour = 20;
    public const double HourHeight = 52;

    private List<ScheduleSlot> _slots = [];
    private bool _loading;

    public override string NavKey => "schedule";
    public override string Title => "Emploi du temps";

    public double GridHeight => (LastHour - FirstHour) * HourHeight;
    public IReadOnlyList<HourMark> Hours { get; } =
        Enumerable.Range(0, LastHour - FirstHour).Select(i => new HourMark($"{FirstHour + i}h", i * HourHeight)).ToList();

    [ObservableProperty] private DateTime _weekStart;
    [ObservableProperty] private string _weekLabel = "";
    [ObservableProperty] private IReadOnlyList<ScheduleDay> _days = [];
    [ObservableProperty] private int _blockCount;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private IReadOnlyList<Option<int?>> _teachers = [];
    [ObservableProperty] private Option<int?>? _selectedTeacher;
    [ObservableProperty] private IReadOnlyList<Option<int?>> _rooms = [];
    [ObservableProperty] private Option<int?>? _selectedRoom;

    partial void OnSelectedTeacherChanged(Option<int?>? value) { if (!_loading) Build(); }
    partial void OnSelectedRoomChanged(Option<int?>? value) { if (!_loading) Build(); }

    private DateTime Now => clock.GetLocalNow().DateTime;

    public override async Task LoadAsync(object? parameter)
    {
        var teacherId = parameter as int?;
        await RunAsync(async () =>
        {
            _loading = true;
            try
            {
                if (WeekStart == default) WeekStart = PlanningWeek.StartOf(Now);
                _slots = await schedule.WeekAsync();
                var previousTeacher = SelectedTeacher is null ? teacherId : SelectedTeacher.Value;
                var previousRoom = SelectedRoom?.Value;
                Teachers = [new Option<int?>(null, "Tous les enseignants"), .. _slots.Select(s => s.Group!.Teacher).Where(t => t is not null)
                    .DistinctBy(t => t!.Id).OrderBy(t => t!.LastName).ThenBy(t => t!.FirstName).Select(t => new Option<int?>(t!.Id, t.FullName))];
                Rooms = [new Option<int?>(null, "Toutes les salles"), .. _slots.Select(RoomOf).Where(r => r is not null)
                    .DistinctBy(r => r!.Id).OrderBy(r => r!.Name).Select(r => new Option<int?>(r!.Id, r.Name))];
                SelectedTeacher = Teachers.FirstOrDefault(t => t.Value == previousTeacher) ?? Teachers[0];
                SelectedRoom = Rooms.FirstOrDefault(r => r.Value == previousRoom) ?? Rooms[0];
            }
            finally
            {
                _loading = false;
            }
            Build();
        }, notifier);
    }

    private static Room? RoomOf(ScheduleSlot s) => s.Room ?? s.Group?.Room;

    private void Build()
    {
        var now = Now;
        WeekLabel = PlanningWeek.Label(WeekStart);
        var slots = _slots
            .Where(s => SelectedTeacher?.Value is not { } t || s.Group!.TeacherId == t)
            .Where(s => SelectedRoom?.Value is not { } r || RoomOf(s)?.Id == r)
            .ToList();
        var hasFriday = _slots.Any(s => s.Day == DayOfWeek.Friday);
        var days = new List<ScheduleDay>();
        for (var i = 0; i < 7; i++)
        {
            var date = WeekStart.AddDays(i);
            var day = date.DayOfWeek;
            var isToday = date == now.Date;
            var visible = day != DayOfWeek.Friday || hasFriday;
            var items = slots.Where(s => s.Day == day).OrderBy(s => s.Start).ThenBy(s => s.End).ToList();
            var (lanes, laneCount) = Layout(items, date, isToday, now);
            var nowHours = now.TimeOfDay.TotalHours;
            days.Add(new ScheduleDay(day, date, $"{Labels.Day(day)} {date.Day}", isToday, visible,
                visible ? Math.Min(2, 1 + (laneCount - 1) * 0.5) : 0, laneCount, lanes,
                (nowHours - FirstHour) * HourHeight, isToday && nowHours >= FirstHour && nowHours <= LastHour));
        }
        Days = days;
        BlockCount = days.Where(d => d.IsVisible).Sum(d => d.Blocks.Count());
        IsEmpty = BlockCount == 0;
    }

    /// <summary>Greedy lane assignment (like the prototype): a block goes to the first lane that is free at its start.</summary>
    private (IReadOnlyList<ScheduleLane> Lanes, int Count) Layout(List<ScheduleSlot> items, DateTime date, bool isToday, DateTime now)
    {
        var laneEnds = new List<TimeSpan>();
        var blocks = new List<ScheduleBlock>();
        foreach (var s in items)
        {
            var lane = laneEnds.FindIndex(end => end <= s.Start);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(s.End);
            }
            else laneEnds[lane] = s.End;
            var g = s.Group!;
            var subject = g.Subject?.Display ?? "?";
            var top = (s.Start.TotalHours - FirstHour) * HourHeight + 2;
            var height = Math.Max(18, (s.End - s.Start).TotalHours * HourHeight - 4);
            var live = isToday && s.Start <= now.TimeOfDay && s.End > now.TimeOfDay;
            blocks.Add(new ScheduleBlock(s.Id, g.Id, g.Id, $"{subject} · {g.Level} {g.Name}",
                $"{Labels.Hour(s.Start)}–{Labels.Hour(s.End)}", RoomOf(s)?.Name ?? "Sans salle", g.Teacher?.FullName ?? "Sans enseignant",
                top, height, lane, live,
                new AsyncRelayCommand(() => nav.NavigateAsync<GroupDetailViewModel>(new GroupDetailViewModel.Target(g.Id))))
            { Start = s.Start, End = s.End });
        }
        var count = Math.Max(1, laneEnds.Count);
        var lanes = Enumerable.Range(0, count).Select(l => new ScheduleLane(blocks.Where(b => b.Lane == l).ToList())).ToList();
        return (lanes, count);
    }

    [RelayCommand]
    private void PreviousWeek()
    {
        WeekStart = WeekStart.AddDays(-7);
        Build();
    }

    [RelayCommand]
    private void NextWeek()
    {
        WeekStart = WeekStart.AddDays(7);
        Build();
    }

    [RelayCommand]
    private void ThisWeek()
    {
        WeekStart = PlanningWeek.StartOf(Now);
        Build();
    }

    [RelayCommand]
    private async Task NewSession()
    {
        var today = Now.Date;
        var date = today >= WeekStart && today < WeekStart.AddDays(7) ? today : WeekStart;
        var dialog = services.GetRequiredService<SessionEditorDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(null, date), notifier)) return;
        if (await dialogs.ShowAsync(dialog)) notifier.Info("Séance créée");
    }
}
