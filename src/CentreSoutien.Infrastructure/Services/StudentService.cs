using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Services;

internal static class Queries
{
    /// <summary>Students with everything billing needs.</summary>
    /// <summary>Students with what billing needs: payments, discount, and each group's timetable and sessions
    /// (sessions count towards the packs). Groups are shared between students (identity resolution).</summary>
    public static IQueryable<Student> StudentsForBilling(this AppDbContext db) => db.Students.AsNoTrackingWithIdentityResolution()
        .Include(s => s.Parent)
        .Include(s => s.Discount)
        .Include(s => s.Payments)
        .Include(s => s.Enrollments).ThenInclude(e => e.Group).ThenInclude(g => g!.Subject)
        .Include(s => s.Enrollments).ThenInclude(e => e.Group).ThenInclude(g => g!.Slots)
        .Include(s => s.Enrollments).ThenInclude(e => e.Group).ThenInclude(g => g!.Sessions)
        .AsSplitQuery();

    /// <summary>Groups with everything teacher earnings and the timetable need.</summary>
    public static IQueryable<Group> GroupsFull(this AppDbContext db) => db.Groups.AsNoTracking()
        .Include(g => g.Subject)
        .Include(g => g.Teacher)
        .Include(g => g.Room)
        .Include(g => g.Slots)
        .Include(g => g.Enrollments)
        .Include(g => g.Sessions)
        .AsSplitQuery();
}

public sealed class StudentService(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : IStudentService
{
    public async Task<List<StudentListItem>> ListAsync(DateTime period, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var students = await db.StudentsForBilling().ToListAsync(ct);
        var now = clock.GetLocalNow().DateTime;
        var today = now.Date;
        var p = Period.Of(period);
        return students
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .Select(s =>
            {
                var current = s.Enrollments.Where(e => e.CoversMonth(p.Year, p.Month)).ToList();
                var courses = string.Join(", ", current.Select(e => e.Group?.Subject?.Display).Where(x => x is not null).Distinct());
                return new StudentListItem(s.Id, s.Matricule, s.FullName, s.Initials, s.Level, courses.Length == 0 ? "—" : courses,
                    s.Parent?.FullName, s.Parent?.Phone ?? s.Phone, Billing.PackPrice(s, now), Billing.Balance(s, now), Billing.State(s, now),
                    s.IsActive, (today - s.EnrolledOn.Date).TotalDays <= 30, s.Discount?.ToString());
            })
            .ToList();
    }

    public async Task<Student?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Students.AsNoTrackingWithIdentityResolution()
            .Include(s => s.Parent)
            .Include(s => s.Discount)
            .Include(s => s.Payments)
            .Include(s => s.Enrollments).ThenInclude(e => e.Group).ThenInclude(g => g!.Subject)
            .Include(s => s.Enrollments).ThenInclude(e => e.Group).ThenInclude(g => g!.Teacher)
            .Include(s => s.Enrollments).ThenInclude(e => e.Group).ThenInclude(g => g!.Room)
            .Include(s => s.Enrollments).ThenInclude(e => e.Group).ThenInclude(g => g!.Slots)
            .Include(s => s.Enrollments).ThenInclude(e => e.Group).ThenInclude(g => g!.Sessions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<Student> SaveAsync(Student student, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(student.FirstName) || string.IsNullOrWhiteSpace(student.LastName))
            throw new BusinessException("Le nom et le prénom de l'élève sont obligatoires.");
        if (string.IsNullOrWhiteSpace(student.Level))
            throw new BusinessException("Le niveau de l'élève est obligatoire.");
        student.FirstName = student.FirstName.Trim();
        student.LastName = student.LastName.Trim();

        await using var db = await factory.CreateDbContextAsync(ct);
        if (string.IsNullOrWhiteSpace(student.Matricule))
            student.Matricule = await NextMatriculeAsync(db, ct);
        else if (await db.Students.AnyAsync(s => s.Matricule == student.Matricule && s.Id != student.Id, ct))
            throw new BusinessException("Ce matricule est déjà attribué à un autre élève.");

        // Only scalar fields are saved here; enrollments and payments have their own operations.
        var entity = new Student();
        if (student.Id != 0)
            entity = await db.Students.FindAsync([student.Id], ct) ?? throw new BusinessException("Élève introuvable.");
        else
            db.Students.Add(entity);
        db.Entry(entity).CurrentValues.SetValues(new
        {
            student.Matricule, student.FirstName, student.LastName, student.BirthDate, student.Gender, student.Level,
            student.School, student.Phone, student.Address, student.PhotoFile, student.IsActive, student.EnrolledOn,
            student.Notes, student.ParentId, student.DiscountId,
        });
        await db.SaveChangesAsync(ct);
        student.Id = entity.Id;
        return student;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.StudentPayments.AnyAsync(p => p.StudentId == id, ct))
            throw new BusinessException("Cet élève a des paiements enregistrés. Rendez-le inactif plutôt que de le supprimer, ou supprimez d'abord ses paiements.");
        var s = await db.Students.FindAsync([id], ct);
        if (s is null) return;
        db.Students.Remove(s);
        await db.SaveChangesAsync(ct);
    }

    public async Task<string> NextMatriculeAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await NextMatriculeAsync(db, ct);
    }

    private static async Task<string> NextMatriculeAsync(AppDbContext db, CancellationToken ct) =>
        "E" + NextMatriculeNumber(await db.Students.Select(s => s.Matricule).ToListAsync(ct));

    /// <summary>Matricule rule shared with the Excel import: E + (highest existing number + 1), at least E1000.</summary>
    internal static int NextMatriculeNumber(IEnumerable<string> existing) =>
        Math.Max(1000, existing.Select(m => int.TryParse(m.TrimStart('E', 'e'), out var n) ? n : 0).DefaultIfEmpty(1000).Max() + 1);

    public async Task EnrollAsync(int studentId, int groupId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var today = clock.GetLocalNow().Date;
        var group = await db.Groups.Include(g => g.Enrollments).FirstOrDefaultAsync(g => g.Id == groupId, ct)
            ?? throw new BusinessException("Groupe introuvable.");
        var student = await db.Students.FindAsync([studentId], ct) ?? throw new BusinessException("Élève introuvable.");
        if (!student.IsActive) throw new BusinessException("Réactivez l'élève avant de l'inscrire.");
        var active = group.Enrollments.Where(e => e.IsActiveOn(today)).ToList();
        if (active.Any(e => e.StudentId == studentId)) throw new BusinessException("L'élève est déjà inscrit dans ce groupe.");
        if (active.Count >= group.Capacity) throw new BusinessException($"Le groupe est complet ({group.Capacity} places).");
        db.Enrollments.Add(new Enrollment { StudentId = studentId, GroupId = groupId, StartDate = today });
        await db.SaveChangesAsync(ct);
    }

    public async Task UnenrollAsync(int studentId, int groupId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var today = clock.GetLocalNow().Date;
        var list = await db.Enrollments.Where(e => e.StudentId == studentId && e.GroupId == groupId).ToListAsync(ct);
        foreach (var e in list.Where(e => e.IsActiveOn(today) || e.StartDate > today))
        {
            // Enrolled by mistake today: remove entirely. Otherwise keep history, ending yesterday
            // (the current month stays billed when the enrollment covered part of it).
            if (e.StartDate.Date >= today) db.Enrollments.Remove(e);
            else e.EndDate = today.AddDays(-1);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task ChangeGroupAsync(int studentId, int fromGroupId, int toGroupId, CancellationToken ct = default)
    {
        if (fromGroupId == toGroupId) return;
        await UnenrollAsync(studentId, fromGroupId, ct);
        try
        {
            await EnrollAsync(studentId, toGroupId, ct);
        }
        catch
        {
            await EnrollAsync(studentId, fromGroupId, ct);
            throw;
        }
    }

    public async Task SetDiscountAsync(int studentId, int? discountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var s = await db.Students.FindAsync([studentId], ct) ?? throw new BusinessException("Élève introuvable.");
        s.DiscountId = discountId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<StudentAttendanceItem>> AttendanceHistoryAsync(int studentId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var list = await db.Attendance.AsNoTracking()
            .Where(a => a.StudentId == studentId)
            .Include(a => a.Session).ThenInclude(s => s!.Group).ThenInclude(g => g!.Subject)
            .ToListAsync(ct);
        return list.OrderByDescending(a => a.Session!.Date).ThenByDescending(a => a.Session!.Start)
            .Select(a => new StudentAttendanceItem(a.Session!.Date, a.Session.Group!.FullName, a.Status)).ToList();
    }

    public async Task<List<StudentGradeItem>> GradesAsync(int studentId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var scale = (await db.Settings.AsNoTracking().FirstAsync(ct)).GradeScale;
        var grades = await db.Grades.AsNoTracking()
            .Where(g => g.StudentId == studentId)
            .Include(g => g.Exam).ThenInclude(e => e!.Group).ThenInclude(g => g!.Subject)
            .ToListAsync(ct);
        return grades.GroupBy(g => g.Exam!.GroupId)
            .Select(grp => new StudentGradeItem(grp.Key, grp.First().Exam!.Group!.FullName,
                grp.OrderBy(g => g.Exam!.Date).Select(g => (g.Exam!.Title, g.Score, g.Exam.MaxScore)).ToList(),
                Grading.Average(grp, scale)))
            .OrderBy(x => x.Group)
            .ToList();
    }

    public async Task<List<string>> LevelsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var levels = await db.Groups.Select(g => g.Level).Union(db.Students.Select(s => s.Level)).ToListAsync(ct);
        return levels.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().OrderBy(LevelOrder).ToList();
    }

    public static int LevelOrder(string level) => Levels.Order(level);
}
