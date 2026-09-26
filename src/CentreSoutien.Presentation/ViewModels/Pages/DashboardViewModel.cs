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

/// <summary>A named entry of an alert (student, group, session…) that opens its page.</summary>
public sealed record AlertItemRow(string Label, string? Value, IRelayCommand Open)
{
    public string Text => string.IsNullOrEmpty(Value) ? Label : $"{Label} · {Value}";
}

/// <summary>One line of the "À traiter" card.</summary>
public sealed record AlertRow(AlertKind Kind, Badge Badge, string Title, string? Detail, string? ActionLabel, IRelayCommand? Action,
    IReadOnlyList<AlertItemRow> Items, string? More)
{
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
    public bool HasAction => Action is not null && ActionLabel is not null;
    public bool HasItems => Items.Count > 0;
    public bool HasMore => More is not null;
}

public sealed partial class DashboardViewModel(
    IDashboardService dashboard, IInsightsService insights, INavigator nav, DialogHost dialogs, TimeProvider clock, IServiceProvider services) : PageViewModel
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

    // "À traiter"
    [ObservableProperty] private IReadOnlyList<AlertRow> _alerts = [];
    [ObservableProperty] private bool _hasNoAlert;
    [ObservableProperty] private string _alertCount = "";

    // Trends
    [ObservableProperty] private ChartData _financeChart = ChartData.Empty;
    [ObservableProperty] private ChartData _collectionChart = ChartData.Empty;
    [ObservableProperty] private ChartData _attendanceChart = ChartData.Empty;
    [ObservableProperty] private ChartData _levelsChart = ChartData.Empty;
    [ObservableProperty] private string _financeSummary = "";
    [ObservableProperty] private string _collectionSummary = "";
    [ObservableProperty] private string _attendanceSummary = "";
    [ObservableProperty] private string _levelsSummary = "";

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

            ApplyInsights(await insights.GetAsync(now));
        });
    }

    private void ApplyInsights(DashboardInsights i)
    {
        Alerts = i.Alerts.Select(a => new AlertRow(a.Kind, BadgeOf(a), a.Title, a.Detail, a.ActionLabel,
            a.Target is null ? null : Go(a.Target),
            a.Items.Select(x => new AlertItemRow(x.Label, x.Value, Go(x.Target))).ToList(),
            a.Count > a.Items.Count && a.Items.Count > 0 ? $"+ {a.Count - a.Items.Count} autre{(a.Count - a.Items.Count > 1 ? "s" : "")}" : null)).ToList();
        HasNoAlert = Alerts.Count == 0;
        AlertCount = Alerts.Count == 0 ? "" : $"{Alerts.Count} point{(Alerts.Count > 1 ? "s" : "")}";

        var months = i.Months.Select(m => ChartLabels.Month(m.Period)).ToList();
        FinanceChart = new ChartData(months,
        [
            new("Recettes", i.Months.Select(m => (double)m.Revenue).ToList(), ChartColor.Accent),
            new("Rémunérations", i.Months.Select(m => (double)m.TeacherPay).ToList(), ChartColor.Warn),
            new("Dépenses", i.Months.Select(m => (double)m.Expenses).ToList(), ChartColor.Muted),
            new("Bénéfice estimé", i.Months.Select(m => (double)m.Profit).ToList(), ChartColor.Ok, ChartSeriesKind.Line),
        ], ChartValueFormat.Money);
        FinanceSummary = $"Bénéfice estimé sur {i.Months.Count} mois : {Money.Format(i.Months.Sum(m => m.Profit))}";

        CollectionChart = new ChartData(months,
            [new("Taux de recouvrement", i.Months.Select(m => m.CollectionRate ?? double.NaN).ToList(), ChartColor.Ok)],
            ChartValueFormat.Percent, 100);
        var current = i.Months.Count > 0 ? i.Months[^1] : null;
        CollectionSummary = current?.CollectionRate is { } rate
            ? $"Ce mois : {rate:0} % ({Money.Format(current.Collected)} sur {Money.Format(current.Expected)})"
            : "Aucune mensualité attendue ce mois";

        AttendanceChart = new ChartData(i.Weeks.Select(w => ChartLabels.Week(w.WeekStart)).ToList(),
            [new("Taux de présence", i.Weeks.Select(w => w.Rate ?? double.NaN).ToList(), ChartColor.Accent, ChartSeriesKind.Line)],
            ChartValueFormat.Percent, 100);
        var marks = i.Weeks.Sum(w => w.Total);
        AttendanceSummary = marks == 0
            ? "Aucun appel enregistré sur la période"
            : $"Moyenne sur {i.Weeks.Count} semaines : {(marks - i.Weeks.Sum(w => w.Absent)) * 100.0 / marks:0} %";

        LevelsChart = new ChartData(i.Levels.Select(l => l.Level).ToList(),
        [
            new("Élèves actifs", i.Levels.Select(l => (double)l.Students).ToList(), ChartColor.Accent),
            new("Inscriptions", i.Levels.Select(l => (double)l.Enrollments).ToList(), ChartColor.Muted),
        ]);
        var students = i.Levels.Sum(l => l.Students);
        LevelsSummary = $"{students} élève{(students > 1 ? "s" : "")} actif{(students > 1 ? "s" : "")} · {i.Levels.Sum(l => l.Enrollments)} inscriptions en cours";
    }

    private static Badge BadgeOf(InsightAlert a) => new(a.Kind switch
    {
        AlertKind.UnpaidStudents => "Impayés",
        AlertKind.AttendanceMissing => "Appel",
        AlertKind.TeacherPayOverdue or AlertKind.TeacherPayDue => "Paie",
        AlertKind.AbsenceThreshold => "Absences",
        AlertKind.BackupOverdue => "Sauvegarde",
        AlertKind.GroupFull => "Complet",
        AlertKind.GroupNearlyFull => "Capacité",
        _ => "Inscription",
    }, a.Severity switch
    {
        AlertSeverity.Critical => BadgeKind.Bad,
        AlertSeverity.Warning => BadgeKind.Warn,
        _ => BadgeKind.Neutral,
    });

    private IRelayCommand Go(InsightTarget t) => new AsyncRelayCommand(() => OpenAsync(t));

    /// <summary>Opens the page an alert points to.</summary>
    public Task OpenAsync(InsightTarget t) => t.Link switch
    {
        InsightLink.Payments => nav.NavigateAsync<PaymentsViewModel>(t.Date),
        InsightLink.Student when t.Id is { } id => nav.NavigateAsync<StudentDetailViewModel>(id),
        InsightLink.Teacher when t.Id is { } id => nav.NavigateAsync<TeacherDetailViewModel>(id),
        InsightLink.TeacherPayments => nav.NavigateAsync<TeacherPaymentsViewModel>(t.Date),
        InsightLink.Course when t.Id is { } id => nav.NavigateAsync<CourseDetailViewModel>(new CourseDetailViewModel.Target(id, t.GroupId)),
        InsightLink.Attendance => nav.NavigateAsync<AttendanceViewModel>(new AttendanceViewModel.Target(t.Date ?? clock.GetLocalNow().Date, t.GroupId, t.SessionId)),
        InsightLink.Settings => nav.NavigateAsync<SettingsViewModel>("backup"),
        _ => Task.CompletedTask,
    };

    [RelayCommand] private Task GoPayments() => nav.NavigateAsync<PaymentsViewModel>();
    [RelayCommand] private Task GoSchedule() => nav.NavigateAsync<ScheduleViewModel>();
    [RelayCommand] private Task GoAttendance() => nav.NavigateAsync<AttendanceViewModel>();
    [RelayCommand] private Task GoReports() => nav.NavigateAsync<ReportsViewModel>();
    [RelayCommand] private Task GoStudents() => nav.NavigateAsync<StudentsViewModel>();

    [RelayCommand]
    private async Task CollectPayment()
    {
        var dialog = services.GetRequiredService<CollectPaymentDialogViewModel>();
        await dialog.InitializeAsync(null);
        if (await dialogs.ShowAsync(dialog)) await LoadAsync(null);
    }
}
