using System.Text.RegularExpressions;
using CentreSoutien.Domain.Enums;

namespace CentreSoutien.Application.Abstractions;

// Data behind the printable school documents (report cards, enrollment certificates, attendance certificates).
// The service gathers the data; the page layout lives in the presentation layer.

/// <summary>Date range covered by a document: one month, or the whole academic year.</summary>
public sealed record DocumentPeriod(DateTime From, DateTime To, string Label, bool IsAcademicYear)
{
    private static readonly string[] MonthNames =
        ["Janvier", "Février", "Mars", "Avril", "Mai", "Juin", "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre"];

    public static DocumentPeriod Month(DateTime anyDay)
    {
        var first = new DateTime(anyDay.Year, anyDay.Month, 1);
        return new DocumentPeriod(first, first.AddMonths(1).AddDays(-1), $"{MonthNames[first.Month - 1]} {first.Year}", false);
    }

    /// <summary>September 1st → August 31st. The start year is read from the settings label (e.g. "2026–2027"), or deduced from today.</summary>
    public static DocumentPeriod AcademicYear(string? academicYear, DateTime today)
    {
        var start = StartYear(academicYear, today);
        return new DocumentPeriod(new DateTime(start, 9, 1), new DateTime(start + 1, 8, 31), $"Année scolaire {start}–{start + 1}", true);
    }

    public static int StartYear(string? academicYear, DateTime today)
    {
        var m = Regex.Match(academicYear ?? "", @"(19|20)\d{2}");
        return m.Success ? int.Parse(m.Value) : today.Month >= 9 ? today.Year : today.Year - 1;
    }

    public bool Contains(DateTime date) => date.Date >= From.Date && date.Date <= To.Date;
}

public sealed record StudentIdentity(
    int Id, string FullName, string Matricule, string Level, string? School, DateTime? BirthDate, Gender Gender,
    string? ParentName, string? ParentPhone, DateTime EnrolledOn);

public sealed record EvaluationLine(string Title, ExamType Type, DateTime Date, decimal? Score, decimal MaxScore, decimal Coefficient);

public sealed record AttendanceSummary(int Present, int Absent, int Late, int Excused)
{
    public static readonly AttendanceSummary Empty = new(0, 0, 0, 0);
    public int Total => Present + Absent + Late + Excused;
    /// <summary>Sessions the student actually attended (present or late).</summary>
    public int Attended => Present + Late;
    /// <summary>Attended / marked sessions, 0–100; null when no session was marked.</summary>
    public double? Rate => Total == 0 ? null : Math.Round(Attended * 100.0 / Total, 1);

    public static AttendanceSummary Of(IEnumerable<AttendanceStatus> statuses)
    {
        var list = statuses.ToList();
        return new AttendanceSummary(list.Count(s => s == AttendanceStatus.Present), list.Count(s => s == AttendanceStatus.Absent),
            list.Count(s => s == AttendanceStatus.Late), list.Count(s => s == AttendanceStatus.Excused));
    }

    public static AttendanceSummary operator +(AttendanceSummary a, AttendanceSummary b) =>
        new(a.Present + b.Present, a.Absent + b.Absent, a.Late + b.Late, a.Excused + b.Excused);
}

/// <summary>One course (group) of a report card.</summary>
public sealed record CourseReport(
    int GroupId, string Course, string Group, string? Teacher, IReadOnlyList<EvaluationLine> Evaluations,
    decimal? Average, int? Rank, int RankedCount, decimal? GroupAverage, AttendanceSummary Attendance);

public sealed record ReportCardData(
    StudentIdentity Student, DocumentPeriod Period, string AcademicYear, IReadOnlyList<CourseReport> Courses,
    decimal? GeneralAverage, AttendanceSummary Attendance, decimal Scale, decimal PassingGrade, int Decimals);

public sealed record CertificateCourse(string Course, string Group, string? Teacher, DateTime Since);

public sealed record CertificateData(StudentIdentity Student, string AcademicYear, IReadOnlyList<CertificateCourse> Courses, DateTime IssuedOn);

public sealed record CourseAttendance(string Course, AttendanceSummary Attendance);

public sealed record AttendanceCertificateData(
    StudentIdentity Student, DocumentPeriod Period, string AcademicYear, IReadOnlyList<CourseAttendance> Courses, AttendanceSummary Total, DateTime IssuedOn);

public interface IAcademicDocumentsService
{
    /// <summary>Report card of one student: every course followed during the period, with evaluations, averages, rank and attendance.</summary>
    Task<ReportCardData> ReportCardAsync(int studentId, DocumentPeriod period, CancellationToken ct = default);
    /// <summary>Report cards of every student enrolled in the group during the period (sorted by name).</summary>
    Task<List<ReportCardData>> GroupReportCardsAsync(int groupId, DocumentPeriod period, CancellationToken ct = default);
    /// <summary>Enrollment certificate: courses the student currently follows for the academic year.</summary>
    Task<CertificateData> CertificateAsync(int studentId, CancellationToken ct = default);
    Task<AttendanceCertificateData> AttendanceCertificateAsync(int studentId, DocumentPeriod period, CancellationToken ct = default);
}

/// <summary>French mentions for an average, thresholds given on /20 and scaled to the configured grading scale.</summary>
public static class Mentions
{
    public static string For(decimal? average, decimal scale, decimal passingGrade)
    {
        if (average is not { } a) return "—";
        var k = scale <= 0 ? 1 : scale / 20m;
        return a >= 16 * k ? "Très bien"
            : a >= 14 * k ? "Bien"
            : a >= 12 * k ? "Assez bien"
            : a >= passingGrade ? "Passable"
            : "Insuffisant";
    }
}
