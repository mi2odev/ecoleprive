using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Services;

/// <summary>Gathers the data of report cards and certificates from the local database.</summary>
public sealed class AcademicDocumentsService(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : IAcademicDocumentsService
{
    public async Task<ReportCardData> ReportCardAsync(int studentId, DocumentPeriod period, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var cards = await BuildReportCardsAsync(db, [studentId], period, ct);
        return cards.FirstOrDefault() ?? throw new BusinessException("Élève introuvable.");
    }

    public async Task<List<ReportCardData>> GroupReportCardsAsync(int groupId, DocumentPeriod period, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var group = await db.Groups.AsNoTracking().Include(g => g.Enrollments).ThenInclude(e => e.Student)
            .FirstOrDefaultAsync(g => g.Id == groupId, ct) ?? throw new BusinessException("Groupe introuvable.");
        var ids = group.Enrollments.Where(e => Overlaps(e, period)).Select(e => e.Student!)
            .DistinctBy(s => s.Id).OrderBy(s => s.LastName).ThenBy(s => s.FirstName).Select(s => s.Id).ToList();
        return await BuildReportCardsAsync(db, ids, period, ct);
    }

    public async Task<CertificateData> CertificateAsync(int studentId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.Settings.AsNoTracking().FirstAsync(ct);
        var student = await LoadStudentsAsync(db, [studentId], ct) is [var s] ? s : throw new BusinessException("Élève introuvable.");
        var today = clock.GetLocalNow().Date;
        var year = DocumentPeriod.AcademicYear(settings.AcademicYear, today);
        // Current (or upcoming) enrollments; if none, those of the academic year.
        var enrollments = student.Enrollments.Where(e => e.IsActiveOn(today) || e.StartDate.Date > today).ToList();
        if (enrollments.Count == 0) enrollments = student.Enrollments.Where(e => Overlaps(e, year)).ToList();
        var courses = enrollments.OrderBy(e => e.Group!.FullName)
            .Select(e => new CertificateCourse(e.Group!.SubjectLevel ?? e.Group.FullName, e.Group.Name, e.Group.Teacher?.FullName, e.StartDate.Date))
            .ToList();
        return new CertificateData(Identity(student), settings.AcademicYear, courses, today);
    }

    public async Task<AttendanceCertificateData> AttendanceCertificateAsync(int studentId, DocumentPeriod period, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.Settings.AsNoTracking().FirstAsync(ct);
        var student = await LoadStudentsAsync(db, [studentId], ct) is [var s] ? s : throw new BusinessException("Élève introuvable.");
        var records = await LoadAttendanceAsync(db, [studentId], period, ct);
        var courses = records.GroupBy(a => a.Session!.GroupId)
            .Select(g => new CourseAttendance(g.First().Session!.Group!.FullName, AttendanceSummary.Of(g.Select(a => a.Status))))
            .OrderBy(c => c.Course).ToList();
        return new AttendanceCertificateData(Identity(student), period, settings.AcademicYear, courses,
            AttendanceSummary.Of(records.Select(a => a.Status)), clock.GetLocalNow().Date);
    }

    private async Task<List<ReportCardData>> BuildReportCardsAsync(AppDbContext db, IReadOnlyList<int> studentIds, DocumentPeriod period, CancellationToken ct)
    {
        if (studentIds.Count == 0) return [];
        var settings = await db.Settings.AsNoTracking().FirstAsync(ct);
        var students = await LoadStudentsAsync(db, studentIds, ct);
        var from = period.From.Date;
        var to = period.To.Date.AddDays(1);

        // Grades of the student during the period, even in a group they have since left.
        var gradedGroupIds = await db.Grades.AsNoTracking()
            .Where(g => studentIds.Contains(g.StudentId) && g.Exam!.Date >= from && g.Exam.Date < to)
            .Select(g => g.Exam!.GroupId).Distinct().ToListAsync(ct);
        var groupIds = students.SelectMany(s => s.Enrollments).Where(e => Overlaps(e, period)).Select(e => e.GroupId)
            .Concat(gradedGroupIds).Distinct().ToList();

        var groups = await db.Groups.AsNoTracking()
            .Include(g => g.Subject)
            .Include(g => g.Teacher)
            .Include(g => g.Enrollments)
            .Where(g => groupIds.Contains(g.Id))
            .AsSplitQuery().ToDictionaryAsync(g => g.Id, ct);
        var exams = await db.Exams.AsNoTracking().Include(e => e.Grades)
            .Where(e => groupIds.Contains(e.GroupId) && e.Date >= from && e.Date < to)
            .ToListAsync(ct);
        foreach (var e in exams)
        foreach (var g in e.Grades)
            g.Exam = e;
        var examsByGroup = exams.GroupBy(e => e.GroupId).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Date).ThenBy(e => e.Title).ToList());
        var attendance = await LoadAttendanceAsync(db, studentIds, period, ct);

        // Group averages of every student, for ranks and the group average.
        var scale = settings.GradeScale;
        var decimals = settings.GradeDecimals;
        var rankings = examsByGroup.ToDictionary(kv => kv.Key, kv => kv.Value.SelectMany(e => e.Grades)
            .GroupBy(g => g.StudentId)
            .Select(g => (StudentId: g.Key, Average: Grading.Average(g, scale, decimals)))
            .Where(x => x.Average is not null)
            .ToDictionary(x => x.StudentId, x => x.Average!.Value));

        var result = new List<ReportCardData>();
        var order = studentIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        foreach (var student in students.OrderBy(s => order[s.Id]))
        {
            var myGroups = groupIds.Where(gid => groups.ContainsKey(gid) && (
                    student.Enrollments.Any(e => e.GroupId == gid && Overlaps(e, period)) ||
                    (examsByGroup.TryGetValue(gid, out var ex) && ex.Any(x => x.Grades.Any(g => g.StudentId == student.Id)))))
                .Select(gid => groups[gid]).OrderBy(g => g.FullName).ToList();
            var courses = new List<CourseReport>();
            foreach (var g in myGroups)
            {
                var groupExams = examsByGroup.GetValueOrDefault(g.Id) ?? [];
                // Evaluations the student sat or should have sat (enrolled on that date).
                var lines = groupExams
                    .Where(e => e.Grades.Any(x => x.StudentId == student.Id) || g.Enrollments.Any(en => en.StudentId == student.Id && en.IsActiveOn(e.Date)))
                    .Select(e => new EvaluationLine(e.Title, e.Type, e.Date, e.Grades.FirstOrDefault(x => x.StudentId == student.Id)?.Score, e.MaxScore, e.Coefficient))
                    .ToList();
                var ranking = rankings.GetValueOrDefault(g.Id) ?? [];
                decimal? avg = ranking.TryGetValue(student.Id, out var a) ? a : null;
                int? rank = avg is null ? null : 1 + ranking.Values.Count(v => v > avg.Value);
                decimal? groupAvg = ranking.Count == 0 ? null : Math.Round(ranking.Values.Average(), decimals);
                var att = AttendanceSummary.Of(attendance.Where(r => r.StudentId == student.Id && r.Session!.GroupId == g.Id).Select(r => r.Status));
                courses.Add(new CourseReport(g.Id, g.SubjectLevel ?? g.FullName, g.Name, g.Teacher?.FullName, lines, avg, rank, ranking.Count, groupAvg, att));
            }
            var averages = courses.Where(c => c.Average is not null).Select(c => c.Average!.Value).ToList();
            decimal? general = averages.Count == 0 ? null : Math.Round(averages.Average(), decimals);
            var total = AttendanceSummary.Of(attendance.Where(r => r.StudentId == student.Id).Select(r => r.Status));
            result.Add(new ReportCardData(Identity(student), period, settings.AcademicYear, courses, general, total, scale, settings.PassingGrade, decimals));
        }
        return result;
    }

    private static async Task<List<Student>> LoadStudentsAsync(AppDbContext db, IReadOnlyList<int> ids, CancellationToken ct) =>
        await db.Students.AsNoTracking()
            .Include(s => s.Parent)
            .Include(s => s.Enrollments).ThenInclude(e => e.Group).ThenInclude(g => g!.Subject)
            .Include(s => s.Enrollments).ThenInclude(e => e.Group).ThenInclude(g => g!.Teacher)
            .Where(s => ids.Contains(s.Id))
            .AsSplitQuery().ToListAsync(ct);

    private static async Task<List<AttendanceRecord>> LoadAttendanceAsync(AppDbContext db, IReadOnlyList<int> studentIds, DocumentPeriod period, CancellationToken ct)
    {
        var from = period.From.Date;
        var to = period.To.Date.AddDays(1);
        return await db.Attendance.AsNoTracking()
            .Include(a => a.Session).ThenInclude(s => s!.Group).ThenInclude(g => g!.Subject)
            .Where(a => studentIds.Contains(a.StudentId) && a.Session!.Date >= from && a.Session.Date < to)
            .ToListAsync(ct);
    }

    private static bool Overlaps(Enrollment e, DocumentPeriod p) =>
        e.StartDate.Date <= p.To.Date && (e.EndDate is null || e.EndDate.Value.Date >= p.From.Date);

    private static StudentIdentity Identity(Student s) => new(
        s.Id, s.FullName, s.Matricule, s.Level, s.School, s.BirthDate, s.Gender, s.Parent?.FullName, s.Parent?.Phone, s.EnrolledOn);
}
