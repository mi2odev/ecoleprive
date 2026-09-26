using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Services;

public sealed class AttendanceService(IDbContextFactory<AppDbContext> factory) : IAttendanceService
{
    public async Task<AttendanceSheet> GetSheetAsync(int sessionId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var session = await db.Sessions.AsNoTracking()
            .Include(s => s.Group).ThenInclude(g => g!.Subject)
            .Include(s => s.Group).ThenInclude(g => g!.Teacher)
            .Include(s => s.Group).ThenInclude(g => g!.Room)
            .Include(s => s.Group).ThenInclude(g => g!.Enrollments).ThenInclude(e => e.Student)
            .Include(s => s.Room).Include(s => s.Teacher)
            .Include(s => s.Attendance).ThenInclude(a => a.Student)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct) ?? throw new BusinessException("Séance introuvable.");

        // Students enrolled on the session date, plus anyone already marked (e.g. since unenrolled).
        var students = session.Group!.Enrollments.Where(e => e.IsActiveOn(session.Date)).Select(e => e.Student!)
            .Concat(session.Attendance.Select(a => a.Student!))
            .DistinctBy(s => s.Id)
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName);
        var lines = students.Select(s =>
        {
            var rec = session.Attendance.FirstOrDefault(a => a.StudentId == s.Id);
            return new AttendanceLine { StudentId = s.Id, Matricule = s.Matricule, FullName = s.FullName, Status = rec?.Status, Note = rec?.Note };
        }).ToList();
        return new AttendanceSheet { Session = session, Lines = lines };
    }

    public async Task SaveAsync(int sessionId, IDictionary<int, AttendanceStatus> marks, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var session = await db.Sessions.Include(s => s.Attendance).FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new BusinessException("Séance introuvable.");
        foreach (var (studentId, status) in marks)
        {
            var rec = session.Attendance.FirstOrDefault(a => a.StudentId == studentId);
            if (rec is null) session.Attendance.Add(new AttendanceRecord { StudentId = studentId, Status = status });
            else rec.Status = status;
        }
        if (session.Status == SessionStatus.Planned && marks.Count > 0) session.Status = SessionStatus.Done;
        await db.SaveChangesAsync(ct);
    }

    public async Task<AttendanceCounts> CountsForDayAsync(DateTime day, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var statuses = await db.Attendance.AsNoTracking().Where(a => a.Session!.Date == day.Date).Select(a => a.Status).ToListAsync(ct);
        return new AttendanceCounts(
            statuses.Count(s => s == AttendanceStatus.Present), statuses.Count(s => s == AttendanceStatus.Absent),
            statuses.Count(s => s == AttendanceStatus.Late), statuses.Count(s => s == AttendanceStatus.Excused));
    }
}

public sealed class ExamService(IDbContextFactory<AppDbContext> factory) : IExamService
{
    public async Task<List<Exam>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var list = await db.Exams.AsNoTracking()
            .Include(e => e.Group).ThenInclude(g => g!.Subject)
            .Include(e => e.Grades)
            .AsSplitQuery().ToListAsync(ct);
        return list.OrderByDescending(e => e.Date).ThenBy(e => e.Title).ToList();
    }

    public async Task<Exam> SaveAsync(Exam exam, CancellationToken ct = default)
    {
        if (exam.GroupId == 0) throw new BusinessException("Choisissez un groupe.");
        if (string.IsNullOrWhiteSpace(exam.Title)) throw new BusinessException("L'intitulé est obligatoire.");
        if (exam.MaxScore <= 0) throw new BusinessException("La note maximale doit être positive.");
        if (exam.Coefficient <= 0) throw new BusinessException("Le coefficient doit être positif.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = exam.Id == 0 ? new Exam() : await db.Exams.FindAsync([exam.Id], ct) ?? throw new BusinessException("Examen introuvable.");
        if (exam.Id == 0) db.Exams.Add(entity);
        db.Entry(entity).CurrentValues.SetValues(new { exam.GroupId, Title = exam.Title.Trim(), exam.Type, exam.Date, exam.MaxScore, exam.Coefficient, exam.Notes });
        await db.SaveChangesAsync(ct);
        exam.Id = entity.Id;
        return exam;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var e = await db.Exams.FindAsync([id], ct);
        if (e is null) return;
        db.Exams.Remove(e);
        await db.SaveChangesAsync(ct);
    }

    public async Task<GradeSheet> GetGradeSheetAsync(int examId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var exam = await db.Exams.AsNoTracking()
            .Include(e => e.Group).ThenInclude(g => g!.Subject)
            .Include(e => e.Group).ThenInclude(g => g!.Enrollments).ThenInclude(en => en.Student)
            .Include(e => e.Grades).ThenInclude(g => g.Student)
            .AsSplitQuery()
            .FirstOrDefaultAsync(e => e.Id == examId, ct) ?? throw new BusinessException("Examen introuvable.");
        var students = exam.Group!.Enrollments.Where(e => e.IsActiveOn(exam.Date)).Select(e => e.Student!)
            .Concat(exam.Grades.Select(g => g.Student!))
            .DistinctBy(s => s.Id).OrderBy(s => s.LastName).ThenBy(s => s.FirstName);
        return new GradeSheet
        {
            Exam = exam,
            Lines = students.Select(s =>
            {
                var g = exam.Grades.FirstOrDefault(x => x.StudentId == s.Id);
                return new GradeLine { StudentId = s.Id, Matricule = s.Matricule, FullName = s.FullName, Score = g?.Score, Comment = g?.Comment };
            }).ToList(),
        };
    }

    public async Task SaveGradesAsync(int examId, IDictionary<int, decimal?> scores, IDictionary<int, string?>? comments = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var exam = await db.Exams.Include(e => e.Grades).FirstOrDefaultAsync(e => e.Id == examId, ct) ?? throw new BusinessException("Examen introuvable.");
        foreach (var (studentId, score) in scores)
        {
            if (score is < 0 || score > exam.MaxScore)
                throw new BusinessException($"Note invalide ({score}). Elle doit être entre 0 et {exam.MaxScore:0.##}.");
            var g = exam.Grades.FirstOrDefault(x => x.StudentId == studentId);
            var comment = comments is not null && comments.TryGetValue(studentId, out var c) ? c : g?.Comment;
            if (g is null)
            {
                if (score is null && string.IsNullOrWhiteSpace(comment)) continue;
                exam.Grades.Add(new Grade { StudentId = studentId, Score = score, Comment = comment });
            }
            else
            {
                g.Score = score;
                g.Comment = comment;
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
