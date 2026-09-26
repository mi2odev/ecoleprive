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

public sealed record DashboardStat(string Value, string Label);

public sealed record DashboardBlock(string Title, IReadOnlyList<DashboardStat> Stats, IRelayCommand Open);

public sealed record TodayRow(string Time, string Name, string Teacher, string Room, Badge State, IRelayCommand Open);

public sealed partial class DashboardViewModel(
    IDashboardService dashboard, INavigator nav, DialogHost dialogs, TimeProvider clock, IServiceProvider services) : PageViewModel
{
    public override string NavKey => "dashboard";
    public override string Title => "Tableau de bord";

    [ObservableProperty] private string _dateLabel = "";
    [ObservableProperty] private IReadOnlyList<Kpi> _finance = [];
    [ObservableProperty] private IReadOnlyList<DashboardBlock> _blocks = [];
    [ObservableProperty] private IReadOnlyList<TodayRow> _today = [];
    [ObservableProperty] private bool _hasNoSessionToday;
    [ObservableProperty] private IReadOnlyList<DashboardStat> _attendance = [];
    [ObservableProperty] private double _presentShare;
    [ObservableProperty] private double _lateShare;
    [ObservableProperty] private double _absentShare;
    [ObservableProperty] private string _nextWhen = "";
    [ObservableProperty] private string _nextName = "Aucune autre séance aujourd'hui";
    [ObservableProperty] private string _nextDetails = "";
    [ObservableProperty] private IReadOnlyList<string> _freeRooms = [];
    [ObservableProperty] private bool _noFreeRoom;

    public override async Task LoadAsync(object? parameter)
    {
        await RunAsync(async () =>
        {
            var now = clock.GetLocalNow().DateTime;
            var d = await dashboard.GetAsync(now);
            DateLabel = Labels.LongDate(now);
            var month = Labels.Month(now);
            Finance =
            [
                new("Recettes du jour", Money.Format(d.RevenueToday), $"{d.ReceiptsToday} reçu{(d.ReceiptsToday > 1 ? "s" : "")} émis"),
                new("Recettes du mois", Money.Format(d.RevenueMonth), month),
                new("Impayés élèves", Money.Format(d.Outstanding), $"{d.OutstandingStudents} élève{(d.OutstandingStudents > 1 ? "s" : "")} concerné{(d.OutstandingStudents > 1 ? "s" : "")}"),
                new("Paiements enseignants", Money.Format(d.TeacherPaymentsDue), "Reste à verser ce mois"),
                new("Dépenses", Money.Format(d.ExpensesMonth), $"{d.ExpenseCount} poste{(d.ExpenseCount > 1 ? "s" : "")} ce mois"),
                new("Bénéfice estimé", Money.Format(d.EstimatedProfit), "Mensualités − charges"),
            ];
            Blocks =
            [
                new("Élèves", [new(d.TotalStudents.ToString(), "Total"), new(d.ActiveStudents.ToString(), "Actifs"), new(d.NewStudents.ToString(), "Nouveaux ce mois")],
                    new AsyncRelayCommand(() => nav.NavigateAsync<StudentsViewModel>())),
                new("Enseignants", [new(d.TotalTeachers.ToString(), "Total"), new(d.ActiveTeachers.ToString(), "Actifs"), new(d.TeacherPaymentsDueCount.ToString(), "Paiements dus")],
                    new AsyncRelayCommand(() => nav.NavigateAsync<TeachersViewModel>())),
                new("Cours", [new(d.ActiveCourses.ToString(), "Cours actifs"), new(d.TodaySessions.Count.ToString(), "Séances aujourd'hui"), new(d.FullGroups.ToString(), "Groupes complets")],
                    new AsyncRelayCommand(() => nav.NavigateAsync<CoursesViewModel>())),
            ];

            var t = now.TimeOfDay;
            Today = d.TodaySessions.Select(s => new TodayRow(
                $"{Labels.Time(s.Start)}–{Labels.Time(s.End)}", s.Group, s.Teacher, s.Room,
                s.End <= t ? new Badge("Terminée", BadgeKind.Outline) : s.Start <= t ? new Badge("En cours", BadgeKind.Ok) : new Badge("À venir", BadgeKind.Outline),
                new AsyncRelayCommand(() => nav.NavigateAsync<AttendanceViewModel>(new AttendanceViewModel.Target(now.Date, s.GroupId, s.SessionId))))).ToList();
            HasNoSessionToday = Today.Count == 0;

            var a = d.AttendanceToday;
            Attendance = [new(a.Present.ToString(), "Présents"), new(a.Absent.ToString(), "Absents"), new(a.Late.ToString(), "Retards")];
            var total = Math.Max(1, a.Present + a.Absent + a.Late);
            PresentShare = (double)a.Present / total;
            LateShare = (double)a.Late / total;
            AbsentShare = (double)a.Absent / total;

            var next = d.TodaySessions.FirstOrDefault(s => s.Start > t);
            if (next is null)
            {
                NextWhen = "Prochaine séance";
                NextName = "Aucune autre séance aujourd'hui";
                NextDetails = "";
            }
            else
            {
                var mins = (int)Math.Round((next.Start - t).TotalMinutes);
                NextWhen = "Prochaine séance · dans " + (mins >= 60 ? $"{mins / 60} h {mins % 60:00}" : $"{mins} min");
                NextName = next.Group;
                NextDetails = $"{Labels.Time(next.Start)}–{Labels.Time(next.End)} · {next.Teacher} · {next.Room}";
            }

            FreeRooms = d.Rooms.Where(r => r.IsFree).Select(r => r.FreeUntil is { } u ? $"{r.Room.Name} · jusqu'à {Labels.Time(u)}" : r.Room.Name).ToList();
            NoFreeRoom = FreeRooms.Count == 0;
        });
    }

    [RelayCommand] private Task GoPayments() => nav.NavigateAsync<PaymentsViewModel>();
    [RelayCommand] private Task GoSchedule() => nav.NavigateAsync<ScheduleViewModel>();
    [RelayCommand] private Task GoAttendance() => nav.NavigateAsync<AttendanceViewModel>();

    [RelayCommand]
    private async Task CollectPayment()
    {
        var dialog = services.GetRequiredService<CollectPaymentDialogViewModel>();
        await dialog.InitializeAsync(null);
        if (await dialogs.ShowAsync(dialog)) await LoadAsync(null);
    }
}
