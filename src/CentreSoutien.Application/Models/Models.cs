using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;

namespace CentreSoutien.Application.Models;

public sealed record StudentListItem(
    int Id, string Matricule, string FullName, string Initials, string Level, string Courses,
    string? ParentName, string? ParentPhone, decimal MonthlyDue, decimal Balance, PaymentState State,
    bool IsActive, bool IsNew, string? DiscountLabel);

public sealed record StudentAttendanceItem(DateTime Date, string Group, AttendanceStatus Status);

public sealed record StudentGradeItem(int GroupId, string Group, IReadOnlyList<(string Exam, decimal? Score, decimal Max)> Scores, decimal? Average);

public sealed record TeacherListItem(int Id, string FullName, string Initials, string Subject, int GroupCount, int StudentCount,
    string Rule, bool IsActive, string? Phone, decimal MonthEarnings, bool PaidThisMonth);

public sealed record TeacherDetail(Teacher Teacher, List<Group> Groups, List<Student> Students, EarningsResult Earnings,
    decimal PaidThisMonth, List<TeacherPayment> Payments);

public sealed record GroupDetail(Group Group, List<Student> Students, decimal Expected, decimal Collected, double AttendanceRate,
    List<Student> EligibleStudents);

public sealed record RoomAvailability(Room Room, bool IsFree, string? OccupiedBy, TimeSpan? FreeUntil);

public sealed class AttendanceSheet
{
    public required Session Session { get; init; }
    public required List<AttendanceLine> Lines { get; init; }
}

public sealed class AttendanceLine
{
    public required int StudentId { get; init; }
    public required string Matricule { get; init; }
    public required string FullName { get; init; }
    public AttendanceStatus? Status { get; set; }
    public string? Note { get; set; }
}

public sealed record AttendanceCounts(int Present, int Absent, int Late, int Excused)
{
    public int Total => Present + Absent + Late + Excused;
}

public sealed class GradeSheet
{
    public required Exam Exam { get; init; }
    public required List<GradeLine> Lines { get; init; }
}

public sealed class GradeLine
{
    public required int StudentId { get; init; }
    public required string Matricule { get; init; }
    public required string FullName { get; init; }
    public decimal? Score { get; set; }
    public string? Comment { get; set; }
}

public sealed record PaymentRow(int StudentId, string Matricule, string FullName, string Level, decimal Gross, decimal Due, decimal Paid,
    decimal Balance, PaymentState State, string? Discount, string? ParentPhone);

public sealed record TeacherPayRow(int TeacherId, string FullName, string Subject, string Rule, decimal Earned, decimal Paid, decimal Remaining,
    DateTime? LastPaymentDate);

public sealed record TodaySession(int? SessionId, int GroupId, string Group, string Teacher, string Room, TimeSpan Start, TimeSpan End);

public sealed class DashboardData
{
    public int TotalStudents { get; init; }
    public int ActiveStudents { get; init; }
    public int NewStudents { get; init; }
    public int TotalTeachers { get; init; }
    public int ActiveTeachers { get; init; }
    public int TeacherPaymentsDueCount { get; init; }
    public int ActiveCourses { get; init; }
    public int ActiveGroups { get; init; }
    public int FullGroups { get; init; }
    public AttendanceCounts AttendanceToday { get; init; } = new(0, 0, 0, 0);
    public decimal RevenueToday { get; init; }
    public int ReceiptsToday { get; init; }
    public decimal RevenueMonth { get; init; }
    public decimal ExpectedMonth { get; init; }
    public decimal Outstanding { get; init; }
    public int OutstandingStudents { get; init; }
    public decimal TeacherPaymentsDue { get; init; }
    public decimal TeacherCostMonth { get; init; }
    public decimal ExpensesMonth { get; init; }
    public int ExpenseCount { get; init; }
    public decimal EstimatedProfit { get; init; }
    public List<TodaySession> TodaySessions { get; init; } = [];
    public List<RoomAvailability> Rooms { get; init; } = [];
}

public sealed class FinanceReport
{
    public DateTime Period { get; init; }
    public decimal Expected { get; init; }
    public decimal Collected { get; init; }
    public decimal RegistrationFees { get; init; }
    public decimal Outstanding { get; init; }
    public decimal TeacherCost { get; init; }
    public decimal TeacherPaid { get; init; }
    public decimal Expenses { get; init; }
    public decimal Profit => Collected + RegistrationFees - TeacherCost - Expenses;
    public List<(string Category, decimal Amount)> ExpensesByCategory { get; init; } = [];
    public List<(string Method, decimal Amount)> CollectedByMethod { get; init; } = [];
    public List<(string Course, decimal Expected, decimal Collected)> ByCourse { get; init; } = [];
    public List<PaymentRow> Unpaid { get; init; } = [];
    public List<TeacherPayRow> Teachers { get; init; } = [];
}

public sealed record GroupAttendanceReport(string Group, int Sessions, int Present, int Absent, int Late, double Rate);

public sealed record GroupGradeReport(string Group, int Exams, decimal? Average, int Passing, int Failing);

public static class Labels
{
    public static string Of(PaymentMethod m) => m switch
    {
        PaymentMethod.Cash => "Espèces",
        PaymentMethod.Ccp => "CCP",
        PaymentMethod.BaridiMob => "BaridiMob",
        PaymentMethod.Cheque => "Chèque",
        _ => "Virement",
    };

    public static string Of(PaymentKind k) => k switch
    {
        PaymentKind.Monthly => "Mensualité",
        PaymentKind.Registration => "Frais d'inscription",
        _ => "Autre",
    };

    public static string Of(PaymentState s) => s switch
    {
        PaymentState.Paid => "Payé",
        PaymentState.Partial => "Partiel",
        PaymentState.Unpaid => "Impayé",
        _ => "Inactif",
    };

    public static string Of(AttendanceStatus s) => s switch
    {
        AttendanceStatus.Present => "Présent",
        AttendanceStatus.Absent => "Absent",
        AttendanceStatus.Late => "Retard",
        _ => "Excusé",
    };

    public static string Of(SessionStatus s) => s switch
    {
        SessionStatus.Planned => "Prévue",
        SessionStatus.Done => "Effectuée",
        _ => "Annulée",
    };

    public static string Of(CompensationType c) => c switch
    {
        CompensationType.Percentage => "Pourcentage des mensualités",
        CompensationType.PerSession => "Tarif par séance",
        _ => "Forfait mensuel",
    };

    public static string Of(ExamType t) => t switch
    {
        ExamType.Test => "Test",
        ExamType.Homework => "Devoir",
        _ => "Examen",
    };

    public static string Of(DocumentOwnerType t) => t switch
    {
        DocumentOwnerType.Center => "Centre",
        DocumentOwnerType.Student => "Élève",
        DocumentOwnerType.Teacher => "Enseignant",
        _ => "Parent",
    };

    public static string Of(DiscountType t) => t == DiscountType.Percent ? "Pourcentage" : "Montant fixe";

    public static string Day(DayOfWeek d) => d switch
    {
        DayOfWeek.Saturday => "Samedi",
        DayOfWeek.Sunday => "Dimanche",
        DayOfWeek.Monday => "Lundi",
        DayOfWeek.Tuesday => "Mardi",
        DayOfWeek.Wednesday => "Mercredi",
        DayOfWeek.Thursday => "Jeudi",
        _ => "Vendredi",
    };

    public static string DayShort(DayOfWeek d) => Day(d)[..3] + ".";

    /// <summary>Algerian school week starts on Saturday.</summary>
    public static readonly DayOfWeek[] WeekOrder =
        [DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public static string Time(TimeSpan t) => t.ToString(@"hh\:mm");

    public static string Slot(ScheduleSlot s) => $"{DayShort(s.Day)} {Hour(s.Start)}–{Hour(s.End)}";

    public static string Hour(TimeSpan t) => t.Minutes == 0 ? $"{t.Hours}h" : $"{t.Hours}h{t.Minutes:00}";

    public static string Slots(IEnumerable<ScheduleSlot> slots) =>
        string.Join(", ", slots.OrderBy(s => Array.IndexOf(WeekOrder, s.Day)).ThenBy(s => s.Start).Select(Slot));

    public static readonly string[] Months =
        ["Janvier", "Février", "Mars", "Avril", "Mai", "Juin", "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre"];

    public static string Month(DateTime period) => $"{Months[period.Month - 1]} {period.Year}";

    public static string LongDate(DateTime d) => $"{Day(d.DayOfWeek)} {d.Day} {Months[d.Month - 1].ToLowerInvariant()} {d.Year}";
}
