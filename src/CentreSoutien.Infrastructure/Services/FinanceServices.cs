using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Services;

public sealed class PaymentService(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : IPaymentService
{
    public async Task<List<PaymentRow>> OverviewAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.GetLocalNow().DateTime;
        var students = await db.StudentsForBilling().Where(s => s.IsActive).ToListAsync(ct);
        return students.OrderBy(s => s.LastName).ThenBy(s => s.FirstName).Select(s => Row(s, now)).ToList();
    }

    internal static PaymentRow Row(Student s, DateTime now)
    {
        var lines = Billing.Allocate(s, now);
        var due = lines.Sum(l => l.Charge.Amount);
        var paid = Billing.Paid(s);
        return new(s.Id, s.Matricule, s.FullName, s.Level, Billing.PackPrice(s, now), due, paid, lines.Sum(l => l.Rest), Billing.State(s, now),
            s.Discount?.ToString(), s.Parent?.Phone ?? s.Phone, Math.Max(0, paid - lines.Sum(l => l.Paid)),
            lines.FirstOrDefault(l => l.Rest > 0)?.Charge.Date,
            string.Join(" · ", Billing.Progress(s, now).Select(x => x.Label)),
            Billing.Accounts(s, now));
    }

    public async Task<StudentPayment> RecordAsync(int studentId, int? groupId, decimal amount, PaymentMethod method, PaymentKind kind, string? note, CancellationToken ct = default)
    {
        if (amount <= 0) throw new BusinessException("Le montant doit être positif.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var student = await db.Students.FindAsync([studentId], ct) ?? throw new BusinessException("Élève introuvable.");
        if (kind != PaymentKind.Sessions) groupId = null;
        else if (groupId is null) throw new BusinessException("Choisissez le groupe payé.");
        else if (!await db.Enrollments.AnyAsync(e => e.StudentId == studentId && e.GroupId == groupId, ct))
            throw new BusinessException("Cet élève n'est pas inscrit dans ce groupe.");
        var settings = await db.Settings.FirstAsync(ct);
        var number = settings.ReceiptPrefix + settings.NextReceiptNumber.ToString("0000");
        while (await db.StudentPayments.IgnoreQueryFilters().AnyAsync(x => x.ReceiptNumber == number, ct))
        {
            settings.NextReceiptNumber++;
            number = settings.ReceiptPrefix + settings.NextReceiptNumber.ToString("0000");
        }
        settings.NextReceiptNumber++;
        var payment = new StudentPayment
        {
            ReceiptNumber = number, StudentId = student.Id, GroupId = groupId, Amount = amount, Method = method, Kind = kind,
            Period = Period.Of(clock.GetLocalNow().DateTime), Date = clock.GetLocalNow().DateTime, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
        db.StudentPayments.Add(payment);
        var group = groupId is null ? null : await db.Groups.Include(g => g.Subject).FirstOrDefaultAsync(g => g.Id == groupId, ct);
        db.AuditLog.Add(Audit.Entry(clock, AuditCategory.Payment,
            $"Reçu {number} : {Money.Format(amount)} encaissés · {student.FullName}",
            string.Join(" · ", new[] { group?.FullName ?? Labels.Of(kind), Labels.Of(method), payment.Note }.Where(x => !string.IsNullOrWhiteSpace(x)))));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        payment.Student = student;
        return payment;
    }

    public async Task<List<StudentPayment>> ReceiptsAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var end = to.Date.AddDays(1);
        var list = await db.StudentPayments.IgnoreQueryFilters().AsNoTracking().Include(p => p.Student).Include(p => p.Group).ThenInclude(g => g!.Subject)
            .Where(p => p.Date >= from.Date && p.Date < end).ToListAsync(ct);
        return list.OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).ToList();
    }

    public async Task<StudentPayment?> GetReceiptAsync(int paymentId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.StudentPayments.IgnoreQueryFilters().AsNoTracking().Include(p => p.Student).ThenInclude(s => s!.Parent)
            .Include(p => p.Group).ThenInclude(g => g!.Subject)
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);
    }

    public async Task CancelAsync(int paymentId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessException("Indiquez le motif de l'annulation.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var p = await db.StudentPayments.Include(x => x.Student).FirstOrDefaultAsync(x => x.Id == paymentId, ct)
            ?? throw new BusinessException("Reçu introuvable ou déjà annulé.");
        p.CancelledAt = clock.GetLocalNow().DateTime;
        p.CancelReason = reason.Trim();
        db.AuditLog.Add(Audit.Entry(clock, AuditCategory.Payment,
            $"Reçu {p.ReceiptNumber} annulé ({Money.Format(p.Amount)} · {p.Student?.FullName})", $"Motif : {p.CancelReason}"));
        await db.SaveChangesAsync(ct);
    }
}

public sealed class TeacherPaymentService(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : ITeacherPaymentService
{
    public async Task<List<TeacherPayRow>> MonthOverviewAsync(DateTime period, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Rows(db, Period.Of(period), clock.GetLocalNow().DateTime, ct);
    }

    internal static async Task<List<TeacherPayRow>> Rows(AppDbContext db, DateTime p, DateTime now, CancellationToken ct)
    {
        var teachers = await db.Teachers.AsNoTracking().Include(t => t.Subject).Include(t => t.Payments).Where(t => t.IsActive).ToListAsync(ct);
        var groups = await db.GroupsFull().ToListAsync(ct);
        return teachers.OrderBy(t => t.LastName).Select(t =>
        {
            var e = TeacherEarnings.Compute(t, groups.Where(g => g.TeacherId == t.Id), p, Money.Format, now);
            var pays = t.Payments.Where(x => Period.Of(x.Period) == p).ToList();
            var paid = pays.Sum(x => x.Amount);
            return new TeacherPayRow(t.Id, t.FullName, t.Subject?.Name ?? "—", e.Rule, e.Amount, paid, Math.Max(0, e.Amount - paid),
                pays.OrderByDescending(x => x.Date).Select(x => (DateTime?)x.Date).FirstOrDefault());
        }).ToList();
    }

    public async Task<TeacherPayment> RecordAsync(int teacherId, decimal amount, PaymentMethod method, DateTime period, string? note, CancellationToken ct = default)
    {
        if (amount <= 0) throw new BusinessException("Le montant doit être positif.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var t = await db.Teachers.FindAsync([teacherId], ct) ?? throw new BusinessException("Enseignant introuvable.");
        var payment = new TeacherPayment
        {
            TeacherId = t.Id, Amount = amount, Method = method, Period = Period.Of(period), Date = clock.GetLocalNow().DateTime,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
        db.TeacherPayments.Add(payment);
        db.AuditLog.Add(Audit.Entry(clock, AuditCategory.TeacherPayment,
            $"Paiement enseignant : {Money.Format(amount)} versés à {t.FullName}", $"{Labels.Month(payment.Period)} · {Labels.Of(method)}"));
        await db.SaveChangesAsync(ct);
        payment.Teacher = t;
        return payment;
    }

    public async Task<List<TeacherPayment>> HistoryAsync(int? teacherId = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.TeacherPayments.AsNoTracking().Include(p => p.Teacher).AsQueryable();
        if (teacherId is not null) q = q.Where(p => p.TeacherId == teacherId);
        return (await q.ToListAsync(ct)).OrderByDescending(p => p.Date).ToList();
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var p = await db.TeacherPayments.Include(x => x.Teacher).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return;
        db.TeacherPayments.Remove(p);
        db.AuditLog.Add(Audit.Entry(clock, AuditCategory.TeacherPayment,
            $"Paiement enseignant supprimé : {Money.Format(p.Amount)} · {p.Teacher?.FullName}", $"{Labels.Month(p.Period)} · versé le {p.Date:dd/MM/yyyy}"));
        await db.SaveChangesAsync(ct);
    }
}

public sealed class DashboardService(IDbContextFactory<AppDbContext> factory, IScheduleService schedule, IAttendanceService attendance) : IDashboardService
{
    public async Task<DashboardData> GetAsync(DateTime now, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var p = Period.Of(now);
        var today = now.Date;
        var students = await db.StudentsForBilling().ToListAsync(ct);
        var active = students.Where(s => s.IsActive).ToList();
        var teachers = await db.Teachers.AsNoTracking().ToListAsync(ct);
        var groups = await db.GroupsFull().Where(g => g.IsActive).ToListAsync(ct);
        var courses = groups.Select(g => (g.SubjectId, g.Level)).Distinct().Count();
        var teacherRows = await TeacherPaymentService.Rows(db, p, now, ct);
        var expenses = (await db.Expenses.AsNoTracking().Where(e => e.Date >= p && e.Date <= Period.End(p)).ToListAsync(ct));
        var monthPayments = await db.StudentPayments.AsNoTracking().Where(x => x.Date >= p && x.Date < p.AddMonths(1)).ToListAsync(ct);
        var todayPayments = monthPayments.Where(x => x.Date.Date == today).ToList();

        // Today's sessions: generated sessions if any, otherwise the timetable.
        var sessions = await db.Sessions.AsNoTracking().Where(s => s.Date == today && s.Status != SessionStatus.Cancelled)
            .Include(s => s.Group).ThenInclude(g => g!.Subject)
            .Include(s => s.Group).ThenInclude(g => g!.Teacher).Include(s => s.Group).ThenInclude(g => g!.Room)
            .Include(s => s.Room).Include(s => s.Teacher).ToListAsync(ct);
        List<TodaySession> todays;
        if (sessions.Count > 0)
            todays = sessions.Select(s => new TodaySession(s.Id, s.GroupId, s.Group!.FullName, (s.Teacher ?? s.Group.Teacher)?.FullName ?? "—",
                (s.Room ?? s.Group.Room)?.Name ?? "—", s.Start, s.End)).ToList();
        else
            todays = groups.SelectMany(g => g.Slots.Where(sl => sl.Day == today.DayOfWeek).Select(sl =>
                new TodaySession(null, g.Id, g.FullName, g.Teacher?.FullName ?? "—", g.Room?.Name ?? "—", sl.Start, sl.End))).ToList();

        // Packs of sessions that started this month (joining students, and every N sessions after that).
        var expected = active.Sum(s => Billing.Charges(s, now).Where(c => c.Date >= p).Sum(c => c.Amount));
        var balances = active.Select(s => Billing.Balance(s, now)).ToList();
        var teacherCost = teacherRows.Sum(r => r.Earned);
        var expenseTotal = expenses.Sum(e => e.Amount);
        return new DashboardData
        {
            TotalStudents = students.Count,
            ActiveStudents = active.Count,
            NewStudents = students.Count(s => s.EnrolledOn >= p),
            TotalTeachers = teachers.Count,
            ActiveTeachers = teachers.Count(t => t.IsActive),
            TeacherPaymentsDueCount = teacherRows.Count(r => r.Remaining > 0),
            ActiveCourses = courses,
            ActiveGroups = groups.Count,
            FullGroups = groups.Count(g => g.Enrollments.Count(e => e.IsActiveOn(today)) >= g.Capacity),
            AttendanceToday = await attendance.CountsForDayAsync(today, ct),
            RevenueToday = todayPayments.Sum(x => x.Amount),
            ReceiptsToday = todayPayments.Count,
            RevenueMonth = monthPayments.Sum(x => x.Amount),
            ExpectedMonth = expected,
            Outstanding = balances.Sum(),
            OutstandingStudents = balances.Count(b => b > 0),
            TeacherPaymentsDue = teacherRows.Sum(r => r.Remaining),
            TeacherCostMonth = teacherCost,
            ExpensesMonth = expenseTotal,
            ExpenseCount = expenses.Count,
            EstimatedProfit = expected - teacherCost - expenseTotal,
            TodaySessions = todays.OrderBy(t => t.Start).ToList(),
            Rooms = await schedule.RoomAvailabilityAsync(now, ct),
        };
    }
}

public sealed class ReportService(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : IReportService
{
    public async Task<FinanceReport> FinanceAsync(DateTime period, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var p = Period.Of(period);
        var now = clock.GetLocalNow().DateTime;
        var end = Period.End(p).AddDays(1);
        // A past month is seen as it stood at its end; the current month as of now.
        var asOf = now < end ? now : end.AddTicks(-1);
        var students = await db.StudentsForBilling().Where(s => s.IsActive).ToListAsync(ct);
        var payments = await db.StudentPayments.AsNoTracking().Where(x => x.Date >= p && x.Date < end).ToListAsync(ct);
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.Date >= p && e.Date <= Period.End(p)).ToListAsync(ct);
        var teachers = await TeacherPaymentService.Rows(db, p, now, ct);

        // Packs that started during the month, and how much of them is paid (oldest packs are paid first).
        var monthLines = students.SelectMany(s => Billing.Allocate(s, asOf)).Where(l => l.Charge.Date >= p).ToList();
        var byCourse = monthLines.GroupBy(l => l.Charge.Group)
            .Select(g => (Course: g.Key, Expected: g.Sum(l => l.Charge.Amount), Collected: g.Sum(l => l.Paid)))
            .Where(x => x.Expected > 0).OrderByDescending(x => x.Expected).ToList();

        return new FinanceReport
        {
            Period = p,
            Expected = monthLines.Sum(l => l.Charge.Amount),
            Collected = payments.Where(x => x.Kind == PaymentKind.Sessions).Sum(x => x.Amount),
            RegistrationFees = payments.Where(x => x.Kind != PaymentKind.Sessions).Sum(x => x.Amount),
            Outstanding = students.Sum(s => Billing.Balance(s, asOf)),
            TeacherCost = teachers.Sum(t => t.Earned),
            TeacherPaid = teachers.Sum(t => t.Paid),
            Expenses = expenses.Sum(e => e.Amount),
            ExpensesByCategory = expenses.GroupBy(e => e.Category).Select(g => (g.Key, g.Sum(e => e.Amount))).OrderByDescending(x => x.Item2).ToList(),
            CollectedByMethod = payments.GroupBy(x => x.Method).Select(g => (Labels.Of(g.Key), g.Sum(x => x.Amount))).OrderByDescending(x => x.Item2).ToList(),
            ByCourse = byCourse,
            Unpaid = students.Select(s => PaymentService.Row(s, asOf)).Where(r => r.Balance > 0).OrderByDescending(r => r.Balance).ToList(),
            Teachers = teachers,
        };
    }

    public async Task<List<GroupAttendanceReport>> AttendanceAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var sessions = await db.Sessions.AsNoTracking().Where(s => s.Date >= from.Date && s.Date <= to.Date && s.Status != SessionStatus.Cancelled)
            .Include(s => s.Attendance).Include(s => s.Group).ThenInclude(g => g!.Subject)
            .AsSplitQuery().ToListAsync(ct);
        return sessions.GroupBy(s => s.GroupId).Select(g =>
        {
            var marks = g.SelectMany(s => s.Attendance).ToList();
            int pr = marks.Count(m => m.Status == AttendanceStatus.Present), ab = marks.Count(m => m.Status == AttendanceStatus.Absent), la = marks.Count(m => m.Status == AttendanceStatus.Late);
            return new GroupAttendanceReport(g.First().Group!.FullName, g.Count(), pr, ab, la, marks.Count == 0 ? 0 : (marks.Count - ab) * 100.0 / marks.Count);
        }).OrderBy(r => r.Group).ToList();
    }

    public async Task<List<GroupGradeReport>> GradesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.Settings.AsNoTracking().FirstAsync(ct);
        var exams = await db.Exams.AsNoTracking().Include(e => e.Grades).Include(e => e.Group).ThenInclude(g => g!.Subject)
            .AsSplitQuery().ToListAsync(ct);
        foreach (var e in exams) foreach (var g in e.Grades) g.Exam = e;
        return exams.GroupBy(e => e.GroupId).Select(g =>
        {
            var grades = g.SelectMany(e => e.Grades).ToList();
            var perStudent = grades.GroupBy(x => x.StudentId).Select(x => Grading.Average(x, settings.GradeScale)).Where(a => a.HasValue).Select(a => a!.Value).ToList();
            return new GroupGradeReport(g.First().Group!.FullName, g.Count(), Grading.Average(grades, settings.GradeScale, settings.GradeDecimals),
                perStudent.Count(a => a >= settings.PassingGrade), perStudent.Count(a => a < settings.PassingGrade));
        }).OrderBy(r => r.Group).ToList();
    }
}
