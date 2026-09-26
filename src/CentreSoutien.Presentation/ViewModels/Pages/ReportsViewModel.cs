using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Presentation.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Presentation.ViewModels.Pages;

public sealed record ReportCourseLine(string Course, string Expected, string Collected, string Rate);
public sealed record ReportAmountLine(string Label, string Amount, string Share);
public sealed record ReportUnpaidLine(string Name, string Level, string Balance, string Phone, IRelayCommand Open);
public sealed record ReportTeacherLine(string Name, string Rule, string Due, string Paid, string Rest);
public sealed record ReportAttendanceLine(string Group, string Sessions, string Present, string Absent, string Late, string Rate);
public sealed record ReportGradeLine(string Group, string Exams, string Average, string Passing, string Failing);

public sealed partial class ReportsViewModel(
    IReportService reports, ISettingsService settings, IPrintService printer, IExportService export, IFilePicker files, IShell shell,
    INavigator nav, INotifier notifier, TimeProvider clock) : PageViewModel, IHasPrintAction
{
    private FinanceReport? _report;

    public override string NavKey => "reports";
    public override string Title => "Rapports";

    [ObservableProperty] private IReadOnlyList<Option<DateTime>> _months = [];
    [ObservableProperty] private Option<DateTime>? _selectedMonth;
    [ObservableProperty] private string _monthLabel = "";
    [ObservableProperty] private IReadOnlyList<Kpi> _summary = [];
    [ObservableProperty] private IReadOnlyList<Kpi> _summaryCosts = [];
    [ObservableProperty] private IReadOnlyList<ReportCourseLine> _byCourse = [];
    [ObservableProperty] private IReadOnlyList<ReportAmountLine> _byMethod = [];
    [ObservableProperty] private IReadOnlyList<ReportAmountLine> _byCategory = [];
    [ObservableProperty] private IReadOnlyList<ReportUnpaidLine> _unpaid = [];
    [ObservableProperty] private string _unpaidTotal = "";
    [ObservableProperty] private IReadOnlyList<ReportTeacherLine> _teachers = [];
    [ObservableProperty] private IReadOnlyList<ReportAttendanceLine> _attendance = [];
    [ObservableProperty] private IReadOnlyList<ReportGradeLine> _grades = [];

    private DateTime Month => SelectedMonth?.Value ?? Period.Of(clock.GetLocalNow().DateTime);

    async partial void OnSelectedMonthChanged(Option<DateTime>? oldValue, Option<DateTime>? newValue)
    {
        if (oldValue is not null && newValue is not null) await ReloadAsync();
    }

    public override async Task LoadAsync(object? parameter)
    {
        if (Months.Count == 0)
        {
            var now = clock.GetLocalNow().DateTime;
            Months = Options.Months(now);
            SelectedMonth = Months.First(m => m.Value == Period.Of(now));
        }
        await ReloadAsync();
    }

    private static string Pct(decimal part, decimal total) => total <= 0 ? "—" : $"{Math.Round(part * 100 / total)} %";

    private Task ReloadAsync() => RunAsync(async () =>
    {
        var month = Month;
        MonthLabel = Labels.Month(month);
        var r = _report = await reports.FinanceAsync(month);
        Summary =
        [
            new("Mensualités attendues", Money.Format(r.Expected), MonthLabel),
            new("Encaissé", Money.Format(r.Collected), "Recouvrement " + Pct(r.Collected, r.Expected)),
            new("Frais d'inscription / autres", Money.Format(r.RegistrationFees)),
            new("Impayés", Money.Format(r.Outstanding), $"{r.Unpaid.Count} élève{(r.Unpaid.Count > 1 ? "s" : "")}"),
        ];
        SummaryCosts =
        [
            new("Rémunération enseignants", Money.Format(r.TeacherCost), "Versé " + Money.Format(r.TeacherPaid)),
            new("Dépenses", Money.Format(r.Expenses), $"{r.ExpensesByCategory.Count} poste{(r.ExpensesByCategory.Count > 1 ? "s" : "")}"),
            new("Résultat", Money.Format(r.Profit), "Encaissements − charges"),
        ];
        ByCourse = r.ByCourse.Select(c => new ReportCourseLine(c.Course, Money.Format(c.Expected), Money.Format(c.Collected), Pct(c.Collected, c.Expected))).ToList();
        var totalMethods = r.CollectedByMethod.Sum(m => m.Amount);
        ByMethod = r.CollectedByMethod.Select(m => new ReportAmountLine(m.Method, Money.Format(m.Amount), Pct(m.Amount, totalMethods))).ToList();
        ByCategory = r.ExpensesByCategory.Select(c => new ReportAmountLine(c.Category, Money.Format(c.Amount), Pct(c.Amount, r.Expenses))).ToList();
        Unpaid = r.Unpaid.Select(u => new ReportUnpaidLine(u.FullName, u.Level, Money.Format(u.Balance), u.ParentPhone ?? "—",
            new AsyncRelayCommand(() => nav.NavigateAsync<StudentDetailViewModel>(u.StudentId)))).ToList();
        UnpaidTotal = $"{r.Unpaid.Count} élève{(r.Unpaid.Count > 1 ? "s" : "")} · {Money.Format(r.Outstanding)}";
        Teachers = r.Teachers.Select(t => new ReportTeacherLine(t.FullName, t.Rule, Money.Format(t.Earned), Money.Format(t.Paid),
            t.Remaining > 0 ? Money.Format(t.Remaining) : "—")).ToList();

        Attendance = (await reports.AttendanceAsync(month, Period.End(month))).Select(a => new ReportAttendanceLine(a.Group, a.Sessions.ToString(),
            a.Present.ToString(), a.Absent.ToString(), a.Late.ToString(), $"{Math.Round(a.Rate)} %")).ToList();
        Grades = (await reports.GradesAsync()).Select(g => new ReportGradeLine(g.Group, g.Exams.ToString(),
            g.Average is null ? "—" : g.Average.Value.ToString("0.00"), g.Passing.ToString(), g.Failing.ToString())).ToList();
    }, notifier);

    [RelayCommand]
    private async Task Print()
    {
        if (_report is null) return;
        await RunAsync(async () =>
        {
            var cfg = await settings.GetAsync();
            var summary = Summary.Concat(SummaryCosts).Select(k => (k.Label, k.Value)).ToList();
            List<PrintTable> tables =
            [
                new("Par groupe", ["Groupe", "Attendu", "Encaissé", "Taux"], ByCourse.Select(c => Row(c.Course, c.Expected, c.Collected, c.Rate)).ToList(), [1, 2, 3]),
                new("Par mode de paiement", ["Mode", "Montant", "Part"], ByMethod.Select(m => Row(m.Label, m.Amount, m.Share)).ToList(), [1, 2]),
                new("Dépenses par catégorie", ["Catégorie", "Montant", "Part"], ByCategory.Select(m => Row(m.Label, m.Amount, m.Share)).ToList(), [1, 2]),
                new("Élèves avec un reste à payer", ["Élève", "Niveau", "Reste", "Téléphone parent"], Unpaid.Select(u => Row(u.Name, u.Level, u.Balance, u.Phone)).ToList(), [2]),
                new("Rémunération des enseignants", ["Enseignant", "Règle", "Dû", "Versé", "Reste"], Teachers.Select(t => Row(t.Name, t.Rule, t.Due, t.Paid, t.Rest)).ToList(), [2, 3, 4]),
                new("Présences par groupe", ["Groupe", "Séances", "Présents", "Absents", "Retards", "Taux"],
                    Attendance.Select(a => Row(a.Group, a.Sessions, a.Present, a.Absent, a.Late, a.Rate)).ToList(), [1, 2, 3, 4, 5]),
                new("Résultats par groupe", ["Groupe", "Évaluations", "Moyenne", "Admis", "En difficulté"],
                    Grades.Select(g => Row(g.Group, g.Exams, g.Average, g.Passing, g.Failing)).ToList(), [1, 2, 3, 4]),
            ];
            printer.PrintReport($"Rapport mensuel — {MonthLabel}", $"{cfg.CenterName} · édité le {clock.GetLocalNow():dd/MM/yyyy HH:mm}", cfg, summary, tables);
        }, notifier);
    }

    private static IReadOnlyList<string> Row(params string[] cells) => cells;

    [RelayCommand]
    private async Task ExportAll()
    {
        var path = files.SaveFile("Exporter toutes les données", $"centre-export-{clock.GetLocalNow():yyyy-MM-dd}.xlsx", "Classeur Excel|*.xlsx");
        if (path is null) return;
        await RunAsync(async () =>
        {
            await export.ExportAllAsync(path);
            notifier.Info("Export Excel généré");
            shell.Reveal(path);
        }, notifier);
    }

    // Ctrl+P in the shell.
    IAsyncRelayCommand? IHasPrintAction.PrintCommand => PrintCommand;
}
