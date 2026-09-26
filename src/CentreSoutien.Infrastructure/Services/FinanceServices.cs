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
    public async Task<List<PaymentRow>> MonthOverviewAsync(DateTime period, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var p = Period.Of(period);
        var students = await db.StudentsForBilling().Where(s => s.IsActive).ToListAsync(ct);
        return students.OrderBy(s => s.LastName).ThenBy(s => s.FirstName).Select(s => Row(s, p)).ToList();
    }

    internal static PaymentRow Row(Student s, DateTime p) => new(
        s.Id, s.Matricule, s.FullName, s.Level, Billing.GrossMonthlyFee(s, p), Billing.MonthlyDue(s, p), Billing.PaidForPeriod(s, p),
        Billing.Balance(s, p), Billing.State(s, p), s.Discount?.ToString(), s.Parent?.Phone ?? s.Phone);

    public async Task<StudentPayment> RecordAsync(int studentId, decimal amount, PaymentMethod method, PaymentKind kind, DateTime period, string? note, CancellationToken ct = default)
    {
        if (amount <= 0) throw new BusinessException("Le montant doit être positif.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var student = await db.Students.FindAsync([studentId], ct) ?? throw new BusinessException("Élève introuvable.");
        var settings = await db.Settings.FirstAsync(ct);
        var number = settings.ReceiptPrefix + settings.NextReceiptNumber.ToString("0000");
        while (await db.StudentPayments.AnyAsync(x => x.ReceiptNumber == number, ct))
        {
            settings.NextReceiptNumber++;
            number = settings.ReceiptPrefix + settings.NextReceiptNumber.ToString("0000");
        }
        settings.NextReceiptNumber++;
        var payment = new StudentPayment
        {
            ReceiptNumber = number, StudentId = student.Id, Amount = amount, Method = method, Kind = kind,
            Period = Period.Of(period), Date = clock.GetLocalNow().DateTime, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
        db.StudentPayments.Add(payment);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        payment.Student = student;
        return payment;
    }

    public async Task<List<StudentPayment>> ReceiptsAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var end = to.Date.AddDays(1);
        var list = await db.StudentPayments.AsNoTracking().Include(p => p.Student)
            .Where(p => p.Date >= from.Date && p.Date < end).ToListAsync(ct);
        return list.OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).ToList();
    }

    public async Task<StudentPayment?> GetReceiptAsync(int paymentId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.StudentPayments.AsNoTracking().Include(p => p.Student).ThenInclude(s => s!.Parent)
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);
    }

    public async Task DeleteAsync(int paymentId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var p = await db.StudentPayments.FindAsync([paymentId], ct);
        if (p is null) return;
        db.StudentPayments.Remove(p);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class TeacherPaymentService(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : ITeacherPaymentService
{
    public async Task<List<TeacherPayRow>> MonthOverviewAsync(DateTime period, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Rows(db, Period.Of(period), ct);
    }

    internal static async Task<List<TeacherPayRow>> Rows(AppDbContext db, DateTime p, CancellationToken ct)
    {
        var teachers = await db.Teachers.AsNoTracking().Include(t => t.Subject).Include(t => t.Payments).Where(t => t.IsActive).ToListAsync(ct);
        var groups = await db.GroupsFull().ToListAsync(ct);
        return teachers.OrderBy(t => t.LastName).Select(t =>
        {
            var e = TeacherEarnings.Compute(t, groups.Where(g => g.TeacherId == t.Id), p, Money.Format);
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
        var p = await db.TeacherPayments.FindAsync([id], ct);
        if (p is null) return;
        db.TeacherPayments.Remove(p);
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
        var teacherRows = await TeacherPaymentService.Rows(db, p, ct);
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

        var expected = active.Sum(s => Billing.MonthlyDue(s, p));
        var collectedMonthly = active.Sum(s => Math.Min(Billing.MonthlyDue(s, p), Billing.PaidForPeriod(s, p)));
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
            Outstanding = active.Sum(s => Billing.Balance(s, p)),
            OutstandingStudents = active.Count(s => Billing.Balance(s, p) > 0),
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

public sealed class ReportService(IDbContextFactory<AppDbContext> factory) : IReportService
{
    public async Task<FinanceReport> FinanceAsync(DateTime period, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var p = Period.Of(period);
        var students = await db.StudentsForBilling().Where(s => s.IsActive).ToListAsync(ct);
        var payments = await db.StudentPayments.AsNoTracking().Where(x => x.Period == p).ToListAsync(ct);
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.Date >= p && e.Date <= Period.End(p)).ToListAsync(ct);
        var teachers = await TeacherPaymentService.Rows(db, p, ct);
        var groups = await db.Groups.AsNoTracking().Include(g => g.Subject).ToListAsync(ct);

        var byCourse = groups.Select(g =>
        {
            decimal exp = 0, col = 0;
            foreach (var s in students)
            {
                var n = s.Enrollments.Count(e => e.GroupId == g.Id && e.CoversMonth(p.Year, p.Month));
                if (n == 0) continue;
                var gross = Billing.GrossMonthlyFee(s, p);
                var due = Billing.MonthlyDue(s, p);
                var share = gross == 0 ? 0 : g.MonthlyPrice * n / gross;
                exp += due * share;
                col += Math.Min(due, Billing.PaidForPeriod(s, p)) * share;
            }
            return (Course: g.FullName, Expected: Math.Round(exp), Collected: Math.Round(col));
        }).Where(x => x.Expected > 0).OrderByDescending(x => x.Expected).ToList();

        return new FinanceReport
        {
            Period = p,
            Expected = students.Sum(s => Billing.MonthlyDue(s, p)),
            Collected = payments.Where(x => x.Kind == PaymentKind.Monthly).Sum(x => x.Amount),
            RegistrationFees = payments.Where(x => x.Kind != PaymentKind.Monthly).Sum(x => x.Amount),
            Outstanding = students.Sum(s => Billing.Balance(s, p)),
            TeacherCost = teachers.Sum(t => t.Earned),
            TeacherPaid = teachers.Sum(t => t.Paid),
            Expenses = expenses.Sum(e => e.Amount),
            ExpensesByCategory = expenses.GroupBy(e => e.Category).Select(g => (g.Key, g.Sum(e => e.Amount))).OrderByDescending(x => x.Item2).ToList(),
            CollectedByMethod = payments.GroupBy(x => x.Method).Select(g => (Labels.Of(g.Key), g.Sum(x => x.Amount))).OrderByDescending(x => x.Item2).ToList(),
            ByCourse = byCourse,
            Unpaid = students.Where(s => Billing.Balance(s, p) > 0).OrderByDescending(s => Billing.Balance(s, p)).Select(s => PaymentService.Row(s, p)).ToList(),
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
