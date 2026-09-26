using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Services;

/// <summary>
/// Dashboard alerts and trends. Everything is loaded once and computed in memory with the domain rules
/// (SQLite cannot sum or sort decimals in SQL).
/// </summary>
public sealed class InsightsService(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : IInsightsService
{
    /// <summary>Number of named entries kept per alert.</summary>
    public const int TopItems = 5;

    /// <summary>A backup older than this is reported as overdue.</summary>
    public static readonly TimeSpan BackupMaxAge = TimeSpan.FromDays(2);

    public async Task<DashboardInsights> GetAsync(DateTime now, int months = 6, int weeks = 8, CancellationToken ct = default)
    {
        if (now == default) now = clock.GetLocalNow().DateTime;
        months = Math.Max(1, months);
        weeks = Math.Max(1, weeks);
        var today = now.Date;
        var period = Period.Of(now);
        var firstMonth = period.AddMonths(-(months - 1));
        var weekStart = WeekStart(today);
        var firstWeek = weekStart.AddDays(-7 * (weeks - 1));
        var from = firstMonth < firstWeek ? firstMonth : firstWeek;
        var end = Period.End(period).AddDays(1);

        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.Settings.AsNoTracking().FirstAsync(ct);
        var students = await db.StudentsForBilling().ToListAsync(ct);
        var groups = await db.GroupsFull().ToListAsync(ct);
        var teachers = await db.Teachers.AsNoTracking().Include(t => t.Payments).Where(t => t.IsActive).ToListAsync(ct);
        var expenses = await db.Expenses.AsNoTracking().Where(e => e.Date >= firstMonth && e.Date < end).ToListAsync(ct);
        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.Date >= from && s.Date < end && s.Status != SessionStatus.Cancelled)
            .Include(s => s.Attendance)
            .Include(s => s.Group).ThenInclude(g => g!.Course).ThenInclude(c => c!.Subject)
            .AsSplitQuery().ToListAsync(ct);
        var todayGenerated = await db.Sessions.AnyAsync(s => s.Date == today, ct);
        var payments = students.SelectMany(s => s.Payments).ToList();

        var monthly = Months(firstMonth, months, students, groups, teachers, expenses, payments, sessions);
        var weekly = Enumerable.Range(0, weeks).Select(i => firstWeek.AddDays(7 * i)).Select(w =>
        {
            var marks = sessions.Where(s => s.Date >= w && s.Date < w.AddDays(7)).SelectMany(s => s.Attendance).ToList();
            return new WeekAttendanceInsight(w, marks.Count(m => m.Status == AttendanceStatus.Present), marks.Count(m => m.Status == AttendanceStatus.Late),
                marks.Count(m => m.Status == AttendanceStatus.Absent), marks.Count(m => m.Status == AttendanceStatus.Excused));
        }).ToList();
        var levels = students.Where(s => s.IsActive).GroupBy(s => string.IsNullOrWhiteSpace(s.Level) ? "—" : s.Level.Trim())
            .Select(g => new LevelInsight(g.Key, g.Count(), g.Sum(s => s.Enrollments.Count(e => e.IsActiveOn(today)))))
            .OrderBy(l => Levels.Order(l.Level)).ThenBy(l => l.Level).ToList();

        var alerts = new List<InsightAlert>();
        Unpaid(alerts, now, period, settings, students);
        AttendanceMissing(alerts, now, groups, sessions, todayGenerated);
        TeacherPay(alerts, now, period, settings, teachers, groups);
        Absences(alerts, period, settings, students, sessions);
        Backup(alerts, now, settings);
        Groups(alerts, today, groups);
        WithoutEnrollment(alerts, today, period, students);
        // Stable sort: most severe first, then the order above (which is the priority within a severity).
        var ordered = alerts.Select((a, i) => (a, i)).OrderByDescending(x => x.a.Severity).ThenBy(x => x.i).Select(x => x.a).ToList();

        return new DashboardInsights(ordered, monthly, weekly, levels);
    }

    /// <summary>Saturday starting the (Algerian) school week of <paramref name="date"/>.</summary>
    public static DateTime WeekStart(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 1) % 7));

    private static List<MonthInsight> Months(DateTime firstMonth, int count, List<Student> students, List<Group> groups, List<Teacher> teachers,
        List<Expense> expenses, List<StudentPayment> payments, List<Session> sessions)
    {
        // Months before the first recorded activity have no estimated teacher pay (the center was not using the application yet).
        var activity = students.SelectMany(s => s.Enrollments).Select(e => e.StartDate)
            .Concat(payments.Select(p => p.Date)).Concat(expenses.Select(e => e.Date)).Concat(sessions.Select(s => s.Date))
            .Concat(teachers.SelectMany(t => t.Payments).Select(p => Period.Of(p.Period)))
            .DefaultIfEmpty(DateTime.MaxValue).Min();
        var active = students.Where(s => s.IsActive).ToList();

        return Enumerable.Range(0, count).Select(i => firstMonth.AddMonths(i)).Select(p =>
        {
            var next = p.AddMonths(1);
            var expected = active.Sum(s => Billing.MonthlyDue(s, p));
            var collected = active.Sum(s => Math.Min(Billing.MonthlyDue(s, p), Billing.PaidForPeriod(s, p)));
            var revenue = payments.Where(x => x.Date >= p && x.Date < next).Sum(x => x.Amount);
            var spent = expenses.Where(e => e.Date >= p && e.Date < next).Sum(e => e.Amount);
            var teacherPay = teachers.Sum(t =>
            {
                var earned = Period.End(p) >= activity
                    ? TeacherEarnings.Compute(t, groups.Where(g => g.TeacherId == t.Id), p, Money.Format).Amount
                    : 0;
                var paid = t.Payments.Where(x => Period.Of(x.Period) == p).Sum(x => x.Amount);
                return Math.Max(earned, paid);
            });
            return new MonthInsight(p, expected, collected, revenue, spent, teacherPay);
        }).ToList();
    }

    private static void Unpaid(List<InsightAlert> alerts, DateTime now, DateTime period, CenterSettings settings, List<Student> students)
    {
        var dueDay = Math.Clamp(settings.PaymentDueDay, 1, DateTime.DaysInMonth(period.Year, period.Month));
        if (now.Day <= dueDay) return;
        var unpaid = students.Where(s => s.IsActive).Select(s => (s, Balance: Billing.Balance(s, period))).Where(x => x.Balance > 0)
            .OrderByDescending(x => x.Balance).ThenBy(x => x.s.LastName).ThenBy(x => x.s.FirstName).ToList();
        if (unpaid.Count == 0) return;
        var n = unpaid.Count;
        alerts.Add(new InsightAlert(AlertKind.UnpaidStudents, AlertSeverity.Critical,
            $"{n} élève{S(n)} n'{(n > 1 ? "ont" : "a")} pas réglé {Labels.Month(period).ToLowerInvariant()}",
            $"{Money.Format(unpaid.Sum(x => x.Balance))} à percevoir · échéance le {dueDay}",
            "Voir les paiements", new InsightTarget(InsightLink.Payments, Date: period),
            unpaid.Take(TopItems).Select(x => new InsightItem(x.s.FullName, Money.Format(x.Balance), new InsightTarget(InsightLink.Student, x.s.Id))).ToList(), n));
    }

    private static void AttendanceMissing(List<InsightAlert> alerts, DateTime now, List<Group> groups, List<Session> sessions, bool todayGenerated)
    {
        var today = now.Date;
        var t = now.TimeOfDay;
        // A group with nobody enrolled has no attendance to take.
        var withStudents = groups.Where(g => g.IsActive && g.Enrollments.Any(e => e.IsActiveOn(today))).Select(g => g.Id).ToHashSet();
        List<InsightItem> items;
        if (todayGenerated)
            items = sessions.Where(s => s.Date.Date == today && s.End <= t && s.Attendance.Count == 0 && withStudents.Contains(s.GroupId)).OrderBy(s => s.Start)
                .Select(s => new InsightItem(s.Group?.FullName ?? "Séance", $"{Labels.Time(s.Start)}–{Labels.Time(s.End)}",
                    new InsightTarget(InsightLink.Attendance, GroupId: s.GroupId, SessionId: s.Id, Date: today))).ToList();
        else // Sessions not generated yet: every finished slot of the timetable is missing its attendance.
            items = groups.Where(g => withStudents.Contains(g.Id)).SelectMany(g => g.Slots.Where(sl => sl.Day == today.DayOfWeek && sl.End <= t).Select(sl => (g, sl)))
                .OrderBy(x => x.sl.Start)
                .Select(x => new InsightItem(x.g.FullName, $"{Labels.Time(x.sl.Start)}–{Labels.Time(x.sl.End)}",
                    new InsightTarget(InsightLink.Attendance, GroupId: x.g.Id, Date: today))).ToList();
        if (items.Count == 0) return;
        var n = items.Count;
        alerts.Add(new InsightAlert(AlertKind.AttendanceMissing, AlertSeverity.Critical,
            $"Appel non fait pour {n} séance{S(n)} terminée{S(n)} aujourd'hui", null,
            "Faire l'appel", items[0].Target, items.Take(TopItems).ToList(), n));
    }

    private static void TeacherPay(List<InsightAlert> alerts, DateTime now, DateTime period, CenterSettings settings, List<Teacher> teachers, List<Group> groups)
    {
        void Add(DateTime p, AlertKind kind, AlertSeverity severity)
        {
            var rows = teachers.Select(t =>
            {
                var earned = TeacherEarnings.Compute(t, groups.Where(g => g.TeacherId == t.Id), p, Money.Format).Amount;
                var paid = t.Payments.Where(x => Period.Of(x.Period) == p).Sum(x => x.Amount);
                return (t, Remaining: Math.Max(0, earned - paid));
            }).Where(x => x.Remaining > 0).OrderByDescending(x => x.Remaining).ThenBy(x => x.t.LastName).ToList();
            if (rows.Count == 0) return;
            var n = rows.Count;
            alerts.Add(new InsightAlert(kind, severity,
                $"{Money.Format(rows.Sum(x => x.Remaining))} à verser à {n} enseignant{S(n)} pour {Labels.Month(p).ToLowerInvariant()}",
                kind == AlertKind.TeacherPayOverdue ? "Mois précédent non soldé" : $"Jour de paie : le {PayDay(settings, p)}",
                "Paiements enseignants", new InsightTarget(InsightLink.TeacherPayments, Date: p),
                rows.Take(TopItems).Select(x => new InsightItem(x.t.FullName, Money.Format(x.Remaining), new InsightTarget(InsightLink.Teacher, x.t.Id))).ToList(), n));
        }

        Add(period.AddMonths(-1), AlertKind.TeacherPayOverdue, AlertSeverity.Critical);
        if (now.Day > PayDay(settings, period)) Add(period, AlertKind.TeacherPayDue, AlertSeverity.Warning);
    }

    private static int PayDay(CenterSettings settings, DateTime p) => Math.Clamp(settings.TeacherPayDay, 1, DateTime.DaysInMonth(p.Year, p.Month));

    private static void Absences(List<InsightAlert> alerts, DateTime period, CenterSettings settings, List<Student> students, List<Session> sessions)
    {
        var threshold = settings.AbsenceAlertThreshold;
        if (threshold <= 0) return;
        var next = period.AddMonths(1);
        var byStudent = students.Where(s => s.IsActive).ToDictionary(s => s.Id);
        var rows = sessions.Where(s => s.Date >= period && s.Date < next).SelectMany(s => s.Attendance)
            .Where(a => a.Status == AttendanceStatus.Absent && byStudent.ContainsKey(a.StudentId))
            .GroupBy(a => a.StudentId).Select(g => (Student: byStudent[g.Key], Count: g.Count()))
            .Where(x => x.Count >= threshold).OrderByDescending(x => x.Count).ThenBy(x => x.Student.LastName).ToList();
        if (rows.Count == 0) return;
        var n = rows.Count;
        alerts.Add(new InsightAlert(AlertKind.AbsenceThreshold, AlertSeverity.Warning,
            $"{n} élève{S(n)} {(n > 1 ? "ont" : "a")} au moins {threshold} absence{S(threshold)} ce mois", "Prévenir les parents",
            n == 1 ? "Voir le profil" : null, n == 1 ? new InsightTarget(InsightLink.Student, rows[0].Student.Id) : null,
            rows.Take(TopItems).Select(x => new InsightItem(x.Student.FullName, $"{x.Count} absence{S(x.Count)}", new InsightTarget(InsightLink.Student, x.Student.Id))).ToList(), n));
    }

    private static void Backup(List<InsightAlert> alerts, DateTime now, CenterSettings settings)
    {
        if (settings.LastBackupAt is { } last && now - last < BackupMaxAge) return;
        var days = settings.LastBackupAt is { } l ? (int)(now.Date - l.Date).TotalDays : 0;
        alerts.Add(new InsightAlert(AlertKind.BackupOverdue, AlertSeverity.Warning,
            settings.LastBackupAt is null ? "Aucune sauvegarde n'a encore été faite" : $"Dernière sauvegarde il y a {days} jour{S(days)}",
            "Faites une sauvegarde, idéalement sur une clé USB", "Sauvegarder", new InsightTarget(InsightLink.Settings), [], 1));
    }

    private static void Groups(List<InsightAlert> alerts, DateTime today, List<Group> groups)
    {
        var fill = groups.Where(g => g.IsActive && g.Capacity > 0 && (g.Course?.IsActive ?? true))
            .Select(g => (g, Enrolled: g.Enrollments.Count(e => e.IsActiveOn(today)))).ToList();
        InsightItem Item((Group g, int Enrolled) x) =>
            new(x.g.FullName, $"{x.Enrolled}/{x.g.Capacity}", new InsightTarget(InsightLink.Course, x.g.CourseId, GroupId: x.g.Id));

        var full = fill.Where(x => x.Enrolled >= x.g.Capacity).OrderByDescending(x => x.Enrolled - x.g.Capacity).ThenBy(x => x.g.FullName).ToList();
        if (full.Count > 0)
            alerts.Add(new InsightAlert(AlertKind.GroupFull, AlertSeverity.Warning,
                $"{full.Count} groupe{S(full.Count)} complet{S(full.Count)}", "Ouvrir un nouveau groupe ou changer de salle",
                null, null, full.Take(TopItems).Select(Item).ToList(), full.Count));

        var nearly = fill.Where(x => x.Enrolled < x.g.Capacity && x.Enrolled * 10 >= x.g.Capacity * 9)
            .OrderByDescending(x => (double)x.Enrolled / x.g.Capacity).ThenBy(x => x.g.FullName).ToList();
        if (nearly.Count > 0)
            alerts.Add(new InsightAlert(AlertKind.GroupNearlyFull, AlertSeverity.Info,
                $"{nearly.Count} groupe{S(nearly.Count)} presque complet{S(nearly.Count)} (≥ 90 %)", null,
                null, null, nearly.Take(TopItems).Select(Item).ToList(), nearly.Count));
    }

    private static void WithoutEnrollment(List<InsightAlert> alerts, DateTime today, DateTime period, List<Student> students)
    {
        var list = students.Where(s => s.IsActive && s.EnrolledOn >= period && !s.Enrollments.Any(e => e.EndDate is null || e.EndDate.Value.Date >= today))
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToList();
        if (list.Count == 0) return;
        var n = list.Count;
        alerts.Add(new InsightAlert(AlertKind.StudentWithoutEnrollment, AlertSeverity.Info,
            n > 1 ? $"{n} nouveaux élèves sans groupe" : "1 nouvel élève sans groupe", "Inscrits ce mois mais dans aucun groupe",
            n == 1 ? "Inscrire" : null, n == 1 ? new InsightTarget(InsightLink.Student, list[0].Id) : null,
            list.Take(TopItems).Select(s => new InsightItem(s.FullName, s.Level, new InsightTarget(InsightLink.Student, s.Id))).ToList(), n));
    }

    private static string S(int n) => n > 1 ? "s" : "";
}
