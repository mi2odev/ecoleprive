using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Services;

public sealed class TeacherService(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : ITeacherService
{
    public async Task<List<TeacherListItem>> ListAsync(DateTime period, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var p = Period.Of(period);
        var teachers = await db.Teachers.AsNoTracking().Include(t => t.Subject).Include(t => t.Payments).ToListAsync(ct);
        var groups = await db.GroupsFull().ToListAsync(ct);
        var now = clock.GetLocalNow().DateTime;
        var today = now.Date;
        return teachers.OrderBy(t => t.LastName).ThenBy(t => t.FirstName).Select(t =>
        {
            var gs = groups.Where(g => g.TeacherId == t.Id && g.IsActive).ToList();
            var students = gs.SelectMany(g => g.Enrollments.Where(e => e.IsActiveOn(today))).Select(e => e.StudentId).Distinct().Count();
            var earned = TeacherEarnings.Compute(t, gs, p, Money.Format, now).Amount;
            var paid = t.Payments.Where(x => Period.Of(x.Period) == p).Sum(x => x.Amount);
            return new TeacherListItem(t.Id, t.FullName, t.Initials, t.Subject?.Name ?? "—", gs.Count, students,
                TeacherEarnings.RuleLabel(t, Money.Format), t.IsActive, t.Phone, earned, paid >= earned && earned > 0);
        }).ToList();
    }

    public async Task<TeacherDetail?> GetDetailAsync(int id, DateTime period, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var t = await db.Teachers.AsNoTracking().Include(x => x.Subject).Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return null;
        var groups = await db.GroupsFull().Where(g => g.TeacherId == id).ToListAsync(ct);
        var today = clock.GetLocalNow().Date;
        var ids = groups.Where(g => g.IsActive).SelectMany(g => g.Enrollments.Where(e => e.IsActiveOn(today))).Select(e => e.StudentId).Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        var p = Period.Of(period);
        var earnings = TeacherEarnings.Compute(t, groups, p, Money.Format, clock.GetLocalNow().DateTime);
        var paid = t.Payments.Where(x => Period.Of(x.Period) == p).Sum(x => x.Amount);
        return new TeacherDetail(t, groups.OrderBy(g => g.FullName).ToList(), students.OrderBy(s => s.LastName).ToList(), earnings, paid,
            t.Payments.OrderByDescending(x => x.Date).ToList());
    }

    public async Task<Teacher?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Teachers.AsNoTracking().Include(t => t.Subject).FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<Teacher> SaveAsync(Teacher teacher, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(teacher.FirstName) || string.IsNullOrWhiteSpace(teacher.LastName))
            throw new BusinessException("Le nom et le prénom de l'enseignant sont obligatoires.");
        if (teacher.CompensationValue < 0 || (teacher.CompensationType == CompensationType.Percentage && teacher.CompensationValue > 100))
            throw new BusinessException("Rémunération invalide.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = teacher.Id == 0 ? new Teacher() : await db.Teachers.FindAsync([teacher.Id], ct) ?? throw new BusinessException("Enseignant introuvable.");
        if (teacher.Id == 0) db.Teachers.Add(entity);
        db.Entry(entity).CurrentValues.SetValues(new
        {
            FirstName = teacher.FirstName.Trim(), LastName = teacher.LastName.Trim(), teacher.Phone, teacher.Email, teacher.Address,
            teacher.SubjectId, teacher.StartYear, teacher.CompensationType, teacher.CompensationValue, teacher.IsActive, teacher.PhotoFile, teacher.Notes,
        });
        db.AuditLog.Add(Audit.Entry(clock, AuditCategory.TeacherPayment,
            $"Enseignant {(teacher.Id == 0 ? "ajouté" : "modifié")} : {teacher.FirstName.Trim()} {teacher.LastName.Trim()}",
            TeacherEarnings.RuleLabel(teacher, Money.Format)));
        await db.SaveChangesAsync(ct);
        teacher.Id = entity.Id;
        return teacher;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.TeacherPayments.AnyAsync(p => p.TeacherId == id, ct))
            throw new BusinessException("Cet enseignant a des paiements enregistrés. Rendez-le inactif plutôt que de le supprimer.");
        var t = await db.Teachers.FindAsync([id], ct);
        if (t is null) return;
        db.Teachers.Remove(t);
        db.AuditLog.Add(Audit.Entry(clock, AuditCategory.TeacherPayment, $"Enseignant supprimé : {t.FullName}"));
        await db.SaveChangesAsync(ct);
    }
}

public sealed class GroupService(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : IGroupService
{
    public async Task<GroupDetail?> GetDetailAsync(int id, DateTime period, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var group = await db.GroupsFull().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return null;
        var p = Period.Of(period);
        var now = clock.GetLocalNow().DateTime;
        var today = now.Date;

        // Revenue of the group this month: its packs of sessions that started this month, and the part already paid
        // (each student's payments cover their oldest packs first).
        var studentIds = group.Enrollments.Select(e => e.StudentId).Distinct().ToList();
        var students = await db.StudentsForBilling().Where(s => studentIds.Contains(s.Id)).ToListAsync(ct);
        var lines = students.Where(s => s.IsActive).SelectMany(s => Billing.Allocate(s, now))
            .Where(l => l.Charge.GroupId == group.Id && l.Charge.Date >= p).ToList();
        var expected = lines.Sum(l => l.Charge.Amount);
        var collected = lines.Sum(l => l.Paid);

        var sessionIds = group.Sessions.Select(s => s.Id).ToList();
        var marks = await db.Attendance.AsNoTracking().Where(a => sessionIds.Contains(a.SessionId)).Select(a => a.Status).ToListAsync(ct);
        var rate = marks.Count == 0 ? 0 : marks.Count(m => m != AttendanceStatus.Absent) * 100.0 / marks.Count;

        var enrolledNow = group.Enrollments.Where(e => e.IsActiveOn(today)).Select(e => e.StudentId).ToHashSet();
        var current = students.Where(s => enrolledNow.Contains(s.Id)).OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToList();
        var eligible = await db.Students.AsNoTracking().Where(s => s.IsActive && s.Level == group.Level).ToListAsync(ct);
        var progress = current.ToDictionary(s => s.Id, s =>
        {
            var e = s.Enrollments.First(x => x.GroupId == group.Id && x.IsActiveOn(today));
            return (Status: Packs.Status(e, now), Balance: Billing.Balance(s, now));
        });
        return new GroupDetail(group, current, Math.Round(expected), Math.Round(collected), rate,
            eligible.Where(s => !enrolledNow.Contains(s.Id)).OrderBy(s => s.LastName).ToList(), progress);
    }

    public async Task<List<Group>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var list = await db.Groups.AsNoTracking()
            .Include(g => g.Subject)
            .Include(g => g.Teacher).Include(g => g.Room).Include(g => g.Slots).Include(g => g.Enrollments)
            .AsSplitQuery().ToListAsync(ct);
        return list.OrderBy(g => g.Subject!.Name).ThenBy(g => StudentService.LevelOrder(g.Level)).ThenBy(g => g.Name).ToList();
    }

    public async Task<Group?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Groups.AsNoTracking()
            .Include(g => g.Subject)
            .Include(g => g.Teacher).Include(g => g.Room).Include(g => g.Slots)
            .Include(g => g.Enrollments).ThenInclude(e => e.Student)
            .AsSplitQuery().FirstOrDefaultAsync(g => g.Id == id, ct);
    }

    public async Task<List<string>> FindConflictsAsync(Group group, IEnumerable<ScheduleSlot> slots, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ConflictsAsync(db, group, slots.ToList(), ct);
    }

    private static async Task<List<string>> ConflictsAsync(AppDbContext db, Group group, List<ScheduleSlot> slots, CancellationToken ct)
    {
        var errors = new List<string>();
        foreach (var s in slots)
            if (s.End <= s.Start) errors.Add($"{Labels.Day(s.Day)} : l'heure de fin doit être après l'heure de début.");
        for (var i = 0; i < slots.Count; i++)
            for (var j = i + 1; j < slots.Count; j++)
                if (slots[i].Overlaps(slots[j])) errors.Add($"Deux créneaux du groupe se chevauchent le {Labels.Day(slots[i].Day).ToLowerInvariant()}.");

        var others = await db.Slots.AsNoTracking().Where(s => s.GroupId != group.Id && s.Group!.IsActive)
            .Include(s => s.Group).ThenInclude(g => g!.Subject)
            .Include(s => s.Group).ThenInclude(g => g!.Teacher)
            .Include(s => s.Group).ThenInclude(g => g!.Room)
            .Include(s => s.Room)
            .ToListAsync(ct);
        foreach (var s in slots)
        {
            var roomId = s.RoomId ?? group.RoomId;
            foreach (var o in others.Where(o => o.Overlaps(s)))
            {
                var oRoom = o.RoomId ?? o.Group!.RoomId;
                if (roomId is not null && oRoom == roomId)
                    errors.Add($"Salle déjà occupée {Labels.Slot(o)} par {o.Group!.FullName}.");
                if (group.TeacherId is not null && o.Group!.TeacherId == group.TeacherId)
                    errors.Add($"L'enseignant a déjà cours {Labels.Slot(o)} ({o.Group.FullName}).");
            }
        }
        return errors.Distinct().ToList();
    }

    public async Task<Group> SaveAsync(Group group, IEnumerable<ScheduleSlot> slots, CancellationToken ct = default)
    {
        if (group.SubjectId == 0) throw new BusinessException("Choisissez une matière.");
        if (string.IsNullOrWhiteSpace(group.Level)) throw new BusinessException("Le niveau est obligatoire.");
        if (group.Price < 0) throw new BusinessException("Le prix doit être positif.");
        if (group.SessionsPerPack is < 1 or > 60) throw new BusinessException("Le nombre de séances par paiement doit être entre 1 et 60.");
        if (string.IsNullOrWhiteSpace(group.Name)) throw new BusinessException("Le nom du groupe est obligatoire.");
        if (group.Capacity <= 0) throw new BusinessException("La capacité doit être positive.");
        var slotList = slots.Select(s => new ScheduleSlot { Day = s.Day, Start = s.Start, End = s.End, RoomId = s.RoomId }).ToList();

        await using var db = await factory.CreateDbContextAsync(ct);
        var level = group.Level.Trim().ToUpperInvariant();
        if (await db.Groups.AnyAsync(g => g.SubjectId == group.SubjectId && g.Level == level && g.Name == group.Name.Trim() && g.Id != group.Id, ct))
            throw new BusinessException("Un groupe porte déjà ce nom pour cette matière et ce niveau.");
        var conflicts = await ConflictsAsync(db, group, slotList, ct);
        if (conflicts.Count > 0) throw new BusinessException(string.Join("\n", conflicts));

        var entity = group.Id == 0 ? new Group() : await db.Groups.Include(g => g.Slots).FirstOrDefaultAsync(g => g.Id == group.Id, ct) ?? throw new BusinessException("Groupe introuvable.");
        if (group.Id == 0) db.Groups.Add(entity);
        db.Entry(entity).CurrentValues.SetValues(new
        {
            group.SubjectId, Level = level, group.Price, group.SessionsPerPack, group.AbsencesCount, Name = group.Name.Trim(), group.TeacherId, group.RoomId,
            group.Capacity, group.IsActive, group.Description,
        });
        db.Slots.RemoveRange(entity.Slots);
        entity.Slots = slotList;
        var subject = await db.Subjects.FindAsync([group.SubjectId], ct);
        db.AuditLog.Add(Audit.Entry(clock, AuditCategory.Group,
            $"Groupe {(group.Id == 0 ? "créé" : "modifié")} : {subject?.Name ?? "?"} · {level} {group.Name.Trim()}",
            $"{Money.Format(group.Price)} / {group.SessionsPerPack} séances · capacité {group.Capacity}{(group.AbsencesCount ? "" : " · absences non comptées")}"));
        await db.SaveChangesAsync(ct);
        group.Id = entity.Id;
        return group;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Enrollments.AnyAsync(e => e.GroupId == id, ct))
            throw new BusinessException("Ce groupe a (ou a eu) des élèves inscrits. Désactivez-le plutôt que de le supprimer.");
        var g = await db.Groups.Include(x => x.Subject).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return;
        db.Groups.Remove(g);
        db.AuditLog.Add(Audit.Entry(clock, AuditCategory.Group, $"Groupe supprimé : {g.FullName}"));
        await db.SaveChangesAsync(ct);
    }
}

public sealed class ScheduleService(IDbContextFactory<AppDbContext> factory) : IScheduleService
{
    public async Task<List<ScheduleSlot>> WeekAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var slots = await db.Slots.AsNoTracking()
            .Where(s => s.Group!.IsActive)
            .Include(s => s.Room)
            .Include(s => s.Group).ThenInclude(g => g!.Subject)
            .Include(s => s.Group).ThenInclude(g => g!.Teacher)
            .Include(s => s.Group).ThenInclude(g => g!.Room)
            .Include(s => s.Group).ThenInclude(g => g!.Enrollments)
            .AsSplitQuery()
            .ToListAsync(ct);
        return slots.OrderBy(s => Array.IndexOf(Labels.WeekOrder, s.Day)).ThenBy(s => s.Start).ToList();
    }

    public async Task<List<RoomAvailability>> RoomAvailabilityAsync(DateTime at, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rooms = (await db.Rooms.AsNoTracking().Where(r => r.IsActive).ToListAsync(ct)).OrderBy(r => r.Name).ToList();
        var day = at.DayOfWeek;
        var time = at.TimeOfDay;
        var slots = (await WeekAsync(ct)).Where(s => s.Day == day).ToList();
        // Sessions of the day take precedence over the timetable (ad hoc sessions, room changes, cancellations).
        var sessions = await db.Sessions.AsNoTracking().Where(s => s.Date == at.Date)
            .Include(s => s.Group).ThenInclude(g => g!.Subject).ToListAsync(ct);
        var occupancy = sessions.Count > 0
            ? sessions.Where(s => s.Status != SessionStatus.Cancelled).Select(s => (Room: s.RoomId ?? s.Group!.RoomId, s.Start, s.End, s.Group!.FullName)).ToList()
            : slots.Select(s => (Room: s.RoomId ?? s.Group!.RoomId, s.Start, s.End, s.Group!.FullName)).ToList();
        return rooms.Select(r =>
        {
            var now = occupancy.FirstOrDefault(o => o.Room == r.Id && o.Start <= time && o.End > time);
            var next = occupancy.Where(o => o.Room == r.Id && o.Start > time).OrderBy(o => o.Start).Select(o => (TimeSpan?)o.Start).FirstOrDefault();
            return now.FullName is null
                ? new RoomAvailability(r, true, null, next)
                : new RoomAvailability(r, false, now.FullName, now.End);
        }).ToList();
    }
}

public sealed class SessionService(IDbContextFactory<AppDbContext> factory) : ISessionService
{
    public async Task<List<Session>> ListAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var list = await db.Sessions.AsNoTracking()
            .Where(s => s.Date >= from.Date && s.Date <= to.Date)
            .Include(s => s.Group).ThenInclude(g => g!.Subject)
            .Include(s => s.Group).ThenInclude(g => g!.Teacher)
            .Include(s => s.Group).ThenInclude(g => g!.Room)
            .Include(s => s.Group).ThenInclude(g => g!.Enrollments)
            .Include(s => s.Room).Include(s => s.Teacher)
            .Include(s => s.Attendance)
            .AsSplitQuery()
            .ToListAsync(ct);
        return list.OrderBy(s => s.Date).ThenBy(s => s.Start).ToList();
    }

    public async Task<int> GenerateAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var slots = await db.Slots.AsNoTracking().Where(s => s.Group!.IsActive).Include(s => s.Group).ToListAsync(ct);
        var existing = (await db.Sessions.AsNoTracking().Where(s => s.Date >= from.Date && s.Date <= to.Date)
            .Select(s => new { s.GroupId, s.Date, s.Start }).ToListAsync(ct))
            .Select(x => (x.GroupId, x.Date.Date, x.Start)).ToHashSet();
        var created = 0;
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
            foreach (var slot in slots.Where(s => s.Day == d.DayOfWeek))
            {
                if (!existing.Add((slot.GroupId, d, slot.Start))) continue;
                db.Sessions.Add(new Session
                {
                    GroupId = slot.GroupId, Date = d, Start = slot.Start, End = slot.End,
                    RoomId = slot.RoomId ?? slot.Group!.RoomId, TeacherId = slot.Group!.TeacherId, Status = SessionStatus.Planned,
                });
                created++;
            }
        await db.SaveChangesAsync(ct);
        return created;
    }

    public async Task<Session> SaveAsync(Session session, CancellationToken ct = default)
    {
        if (session.GroupId == 0) throw new BusinessException("Choisissez un groupe.");
        if (session.End <= session.Start) throw new BusinessException("L'heure de fin doit être après l'heure de début.");
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.Sessions.AnyAsync(s => s.GroupId == session.GroupId && s.Date == session.Date.Date && s.Start == session.Start && s.Id != session.Id, ct))
            throw new BusinessException("Une séance existe déjà pour ce groupe à cette date et heure.");
        var entity = session.Id == 0 ? new Session() : await db.Sessions.FindAsync([session.Id], ct) ?? throw new BusinessException("Séance introuvable.");
        if (session.Id == 0) db.Sessions.Add(entity);
        db.Entry(entity).CurrentValues.SetValues(new
        {
            session.GroupId, Date = session.Date.Date, session.Start, session.End, session.RoomId, session.TeacherId, session.Status, session.Topic,
        });
        await db.SaveChangesAsync(ct);
        session.Id = entity.Id;
        return session;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var s = await db.Sessions.FindAsync([id], ct);
        if (s is null) return;
        db.Sessions.Remove(s);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetStatusAsync(int id, SessionStatus status, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var s = await db.Sessions.FindAsync([id], ct) ?? throw new BusinessException("Séance introuvable.");
        s.Status = status;
        await db.SaveChangesAsync(ct);
    }
}
