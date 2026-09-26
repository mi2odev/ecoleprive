namespace CentreSoutien.Application.Abstractions;

/// <summary>Dashboard insights: prioritized "to do" alerts and the trend series behind the dashboard charts.</summary>
public interface IInsightsService
{
    /// <summary>
    /// Alerts as of <paramref name="now"/>, the last <paramref name="months"/> months (current one included)
    /// and the last <paramref name="weeks"/> school weeks (Saturday → Friday, current one included).
    /// </summary>
    Task<DashboardInsights> GetAsync(DateTime now, int months = 6, int weeks = 8, CancellationToken ct = default);
}

public sealed record DashboardInsights(
    IReadOnlyList<InsightAlert> Alerts,
    IReadOnlyList<MonthInsight> Months,
    IReadOnlyList<WeekAttendanceInsight> Weeks,
    IReadOnlyList<LevelInsight> Levels);

/// <summary>Finance of one month. Revenue and expenses are cash received / spent that month; teacher pay is what the month costs.</summary>
public sealed record MonthInsight(DateTime Period, decimal Expected, decimal Collected, decimal Revenue, decimal Expenses, decimal TeacherPay)
{
    /// <summary>Estimated result: revenue − expenses − teacher pay.</summary>
    public decimal Profit => Revenue - Expenses - TeacherPay;

    /// <summary>Share (0–100) of the month's fees collected, or null when nothing was due.</summary>
    public double? CollectionRate => Expected > 0 ? (double)Math.Min(100m, Collected * 100m / Expected) : null;
}

/// <summary>Attendance marks of the sessions of one week (starting on Saturday).</summary>
public sealed record WeekAttendanceInsight(DateTime WeekStart, int Present, int Late, int Absent, int Excused)
{
    public int Total => Present + Late + Absent + Excused;

    /// <summary>Share (0–100) of marks that are not absences, or null when no attendance was taken.</summary>
    public double? Rate => Total == 0 ? null : (Total - Absent) * 100.0 / Total;
}

/// <summary>Active students of a level and their number of current enrollments.</summary>
public sealed record LevelInsight(string Level, int Students, int Enrollments);

public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}

public enum AlertKind
{
    UnpaidStudents,
    AttendanceMissing,
    TeacherPayOverdue,
    TeacherPayDue,
    AbsenceThreshold,
    BackupOverdue,
    GroupFull,
    GroupNearlyFull,
    StudentWithoutEnrollment,
}

/// <summary>Where an alert or one of its items leads.</summary>
public enum InsightLink
{
    Payments,
    Student,
    Teacher,
    TeacherPayments,
    Course,
    Attendance,
    Settings,
}

/// <summary>Navigation target. <see cref="Id"/> is the student / teacher / course id; <see cref="Date"/> the attendance day or billing month.</summary>
public sealed record InsightTarget(InsightLink Link, int? Id = null, int? GroupId = null, int? SessionId = null, DateTime? Date = null);

/// <summary>One concrete entry of an alert (a student, a group, a session…).</summary>
public sealed record InsightItem(string Label, string? Value, InsightTarget Target);

/// <summary>An actionable alert. <see cref="Count"/> is the number of things concerned (items may be truncated).</summary>
public sealed record InsightAlert(
    AlertKind Kind,
    AlertSeverity Severity,
    string Title,
    string? Detail,
    string? ActionLabel,
    InsightTarget? Target,
    IReadOnlyList<InsightItem> Items,
    int Count);
