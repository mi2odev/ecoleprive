using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;

namespace CentreSoutien.Domain.Calculations;

public static class Period
{
    public static DateTime Of(DateTime date) => new(date.Year, date.Month, 1);
    public static DateTime Current => Of(DateTime.Today);
    public static DateTime End(DateTime period) => Of(period).AddMonths(1).AddDays(-1);
}

/// <summary>Pure billing rules. Callers pass entity graphs with the needed navigation properties loaded.</summary>
public static class Billing
{
    /// <summary>Gross monthly fee before discount: sum of the course prices of every group the student is enrolled in that month.</summary>
    public static decimal GrossMonthlyFee(Student student, DateTime period)
    {
        var p = Period.Of(period);
        return student.Enrollments
            .Where(e => e.CoversMonth(p.Year, p.Month))
            .Sum(e => e.Group?.Course?.MonthlyPrice ?? 0m);
    }

    /// <summary>Amount due for the month after the student's discount.</summary>
    public static decimal MonthlyDue(Student student, DateTime period)
    {
        if (!student.IsActive) return 0;
        var gross = GrossMonthlyFee(student, period);
        return student.Discount is { IsActive: true } d ? d.Apply(gross) : gross;
    }

    public static decimal PaidForPeriod(Student student, DateTime period)
    {
        var p = Period.Of(period);
        return student.Payments.Where(x => x.Kind == PaymentKind.Monthly && Period.Of(x.Period) == p).Sum(x => x.Amount);
    }

    public static decimal Balance(Student student, DateTime period) =>
        Math.Max(0, MonthlyDue(student, period) - PaidForPeriod(student, period));

    public static PaymentState State(Student student, DateTime period)
    {
        if (!student.IsActive) return PaymentState.Inactive;
        var due = MonthlyDue(student, period);
        var paid = PaidForPeriod(student, period);
        if (due <= 0 || paid >= due) return PaymentState.Paid;
        return paid > 0 ? PaymentState.Partial : PaymentState.Unpaid;
    }
}

public enum PaymentState
{
    Paid,
    Partial,
    Unpaid,
    Inactive,
}

public sealed record EarningsResult(decimal Amount, string Rule, int Basis);

/// <summary>Teacher compensation rules.</summary>
public static class TeacherEarnings
{
    /// <summary>
    /// Earnings for the month. Groups must have Course, Enrollments, Slots and Sessions loaded.
    /// Per-session pay counts the non-cancelled sessions of the month; when no session has been
    /// generated yet for the month it falls back to the number of timetable occurrences.
    /// </summary>
    public static EarningsResult Compute(Teacher teacher, IEnumerable<Group> teacherGroups, DateTime period, Func<decimal, string> money)
    {
        var p = Period.Of(period);
        var groups = teacherGroups.Where(g => g.IsActive).ToList();
        switch (teacher.CompensationType)
        {
            case CompensationType.Percentage:
            {
                var enrolled = groups.SelectMany(g => g.Enrollments.Where(e => e.CoversMonth(p.Year, p.Month)).Select(e => (g, e))).ToList();
                var baseAmount = enrolled.Sum(x => x.g.Course?.MonthlyPrice ?? 0);
                var students = enrolled.Select(x => x.e.StudentId).Distinct().Count();
                return new(Math.Round(baseAmount * teacher.CompensationValue / 100m, 0),
                    $"{teacher.CompensationValue:0.##} % des mensualités de {students} élève{(students > 1 ? "s" : "")}", students);
            }
            case CompensationType.PerSession:
            {
                var count = CountSessions(teacher, groups, p);
                return new(count * teacher.CompensationValue, $"{count} séance{(count > 1 ? "s" : "")} × {money(teacher.CompensationValue)}", count);
            }
            default:
                return new(teacher.CompensationValue, "Forfait mensuel", 0);
        }
    }

    public static int CountSessions(Teacher teacher, IEnumerable<Group> groups, DateTime period)
    {
        var p = Period.Of(period);
        var end = Period.End(p);
        var list = groups.ToList();
        var sessions = list.SelectMany(g => g.Sessions)
            .Where(s => s.Date >= p && s.Date <= end && s.Status != SessionStatus.Cancelled && (s.TeacherId ?? s.Group?.TeacherId ?? teacher.Id) == teacher.Id)
            .ToList();
        if (list.SelectMany(g => g.Sessions).Any(s => s.Date >= p && s.Date <= end))
            return sessions.Count;
        // Projection from the weekly timetable.
        var count = 0;
        for (var d = p; d <= end; d = d.AddDays(1))
            count += list.SelectMany(g => g.Slots).Count(s => s.Day == d.DayOfWeek);
        return count;
    }

    public static string RuleLabel(Teacher t, Func<decimal, string> money) => t.CompensationType switch
    {
        CompensationType.Percentage => $"{t.CompensationValue:0.##} % des mensualités",
        CompensationType.PerSession => $"{money(t.CompensationValue)} / séance",
        _ => $"{money(t.CompensationValue)} / mois",
    };
}

public static class Grading
{
    /// <summary>Weighted average normalised to the grading scale (e.g. /20). Returns null when no scores exist.</summary>
    public static decimal? Average(IEnumerable<Grade> grades, decimal scale, int decimals = 2)
    {
        var list = grades.Where(g => g.Score.HasValue && g.Exam is not null && g.Exam.MaxScore > 0).ToList();
        if (list.Count == 0) return null;
        var totalCoef = list.Sum(g => g.Exam!.Coefficient);
        if (totalCoef <= 0) return null;
        var sum = list.Sum(g => g.Score!.Value / g.Exam!.MaxScore * scale * g.Exam.Coefficient);
        return Math.Round(sum / totalCoef, decimals);
    }
}
