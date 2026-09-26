using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Tests;

public class InsightsServiceTests
{
    /// <summary>Saturday 26 September 2026, 14:32 (same as the test clock).</summary>
    private static readonly DateTime Now = new(2026, 9, 26, 14, 32, 0);

    private sealed record Scenario(int GroupId, int Alice, int Bilal, int Chahra, int TeacherId, int TodaySessionId);

    /// <summary>
    /// Six months (April → September 2026) of a tiny center: one 4 000 DZD group of two (full), a fixed-salary teacher.
    /// Alice pays every month, Bilal skips September and misses three sessions, Chahra joined this month without a group.
    /// August's teacher pay is not recorded and today's morning session has no attendance.
    /// </summary>
    private static async Task<Scenario> SeedAsync(TestHost host)
    {
        await using var db = await host.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var april = new DateTime(2026, 4, 1);
        var subject = new Subject { Name = "Mathématiques" };
        var teacher = new Teacher { FirstName = "Karima", LastName = "Boudiaf", CompensationType = CompensationType.FixedMonthly, CompensationValue = 20000 };
        var group = new Group
        {
            Subject = subject, Level = "3AS", MonthlyPrice = 4000, Name = "A", Teacher = teacher, Capacity = 2,
            Slots = [new ScheduleSlot { Day = DayOfWeek.Saturday, Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(11) }],
        };
        var alice = new Student { Matricule = "E1", FirstName = "Alice", LastName = "Amrani", Level = "3AS", EnrolledOn = april };
        var bilal = new Student { Matricule = "E2", FirstName = "Bilal", LastName = "Benali", Level = "3AS", EnrolledOn = april };
        var chahra = new Student { Matricule = "E3", FirstName = "Chahra", LastName = "Cherif", Level = "3AS", EnrolledOn = new DateTime(2026, 9, 10) };
        group.Enrollments.Add(new Enrollment { Student = alice, StartDate = april });
        group.Enrollments.Add(new Enrollment { Student = bilal, StartDate = april });
        db.AddRange(group, chahra);

        var receipt = 1;
        for (var m = 0; m < 6; m++)
        {
            var p = april.AddMonths(m);
            db.StudentPayments.Add(new StudentPayment { ReceiptNumber = $"R{receipt++}", Student = alice, Amount = 4000, Period = p, Date = p.AddDays(2), Kind = PaymentKind.Monthly });
            if (p.Month != 9)
                db.StudentPayments.Add(new StudentPayment { ReceiptNumber = $"R{receipt++}", Student = bilal, Amount = 4000, Period = p, Date = p.AddDays(3), Kind = PaymentKind.Monthly });
            db.Expenses.Add(new Expense { Date = p.AddDays(4), Category = "Loyer", Description = "Loyer", Amount = 10000 });
            if (p.Month <= 7) db.TeacherPayments.Add(new TeacherPayment { Teacher = teacher, Period = p, Date = p.AddMonths(1).AddDays(-1), Amount = 20000 });
        }
        await db.SaveChangesAsync();

        // Three Saturday sessions in September: Bilal absent each time. One in August with both present.
        foreach (var day in new[] { 5, 12, 19 })
            db.Sessions.Add(new Session
            {
                GroupId = group.Id, Date = new DateTime(2026, 9, day), Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(11), Status = SessionStatus.Done,
                Attendance = [new AttendanceRecord { StudentId = alice.Id, Status = AttendanceStatus.Present }, new AttendanceRecord { StudentId = bilal.Id, Status = AttendanceStatus.Absent }],
            });
        db.Sessions.Add(new Session
        {
            GroupId = group.Id, Date = new DateTime(2026, 8, 29), Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(11), Status = SessionStatus.Done,
            Attendance = [new AttendanceRecord { StudentId = alice.Id, Status = AttendanceStatus.Present }, new AttendanceRecord { StudentId = bilal.Id, Status = AttendanceStatus.Late }],
        });
        var today = new Session { GroupId = group.Id, Date = Now.Date, Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(11) };
        db.Sessions.Add(today);
        await db.SaveChangesAsync();
        return new Scenario(group.Id, alice.Id, bilal.Id, chahra.Id, teacher.Id, today.Id);
    }

    [Fact]
    public async Task Months_cover_the_last_six_months_with_revenue_costs_and_collection_rate()
    {
        await using var host = await TestHost.CreateAsync();
        await SeedAsync(host);
        var insights = await host.Get<IInsightsService>().GetAsync(Now);

        Assert.Equal(Enumerable.Range(4, 6).Select(m => new DateTime(2026, m, 1)), insights.Months.Select(m => m.Period));
        var april = insights.Months[0];
        Assert.Equal(8000, april.Revenue);
        Assert.Equal(10000, april.Expenses);
        Assert.Equal(20000, april.TeacherPay);
        Assert.Equal(-22000, april.Profit);
        Assert.Equal(100, april.CollectionRate);

        var september = insights.Months[^1];
        Assert.Equal(8000, september.Expected);
        Assert.Equal(4000, september.Collected);
        Assert.Equal(4000, september.Revenue);
        Assert.Equal(50, september.CollectionRate);
        Assert.Equal(20000, september.TeacherPay); // earned even though not paid yet
    }

    [Fact]
    public async Task Weeks_start_on_saturday_and_give_the_attendance_rate()
    {
        await using var host = await TestHost.CreateAsync();
        await SeedAsync(host);
        var weeks = (await host.Get<IInsightsService>().GetAsync(Now)).Weeks;

        Assert.Equal(8, weeks.Count);
        Assert.All(weeks, w => Assert.Equal(DayOfWeek.Saturday, w.WeekStart.DayOfWeek));
        Assert.Equal(Now.Date, weeks[^1].WeekStart);
        Assert.Null(weeks[^1].Rate); // today's session: no attendance yet
        var sep19 = weeks.Single(w => w.WeekStart == new DateTime(2026, 9, 19));
        Assert.Equal(50, sep19.Rate);
        var aug29 = weeks.Single(w => w.WeekStart == new DateTime(2026, 8, 29));
        Assert.Equal(100, aug29.Rate); // late counts as attended
        Assert.Equal(1, aug29.Late);
    }

    [Fact]
    public async Task Alerts_cover_every_rule_most_severe_first()
    {
        await using var host = await TestHost.CreateAsync();
        var s = await SeedAsync(host);
        var insights = await host.Get<IInsightsService>().GetAsync(Now);
        var alerts = insights.Alerts;
        InsightAlert Of(AlertKind k) => Assert.Single(alerts, a => a.Kind == k);

        var unpaid = Of(AlertKind.UnpaidStudents);
        Assert.Equal(AlertSeverity.Critical, unpaid.Severity);
        Assert.Equal(1, unpaid.Count);
        var item = Assert.Single(unpaid.Items);
        Assert.Equal("Bilal Benali", item.Label);
        Assert.Equal(new InsightTarget(InsightLink.Student, s.Bilal), item.Target);
        Assert.Equal(InsightLink.Payments, unpaid.Target!.Link);

        var absences = Of(AlertKind.AbsenceThreshold);
        Assert.Equal(AlertSeverity.Warning, absences.Severity);
        Assert.Equal("3 absences", Assert.Single(absences.Items).Value);
        Assert.Equal(new InsightTarget(InsightLink.Student, s.Bilal), absences.Target);

        var attendance = Of(AlertKind.AttendanceMissing);
        Assert.Equal(new InsightTarget(InsightLink.Attendance, GroupId: s.GroupId, SessionId: s.TodaySessionId, Date: Now.Date), attendance.Target);

        var teacherPay = Of(AlertKind.TeacherPayOverdue);
        Assert.Contains("août 2026", teacherPay.Title);
        Assert.Equal(new InsightTarget(InsightLink.TeacherPayments, Date: new DateTime(2026, 8, 1)), teacherPay.Target);
        Assert.Equal(new InsightTarget(InsightLink.Teacher, s.TeacherId), Assert.Single(teacherPay.Items).Target);
        Assert.DoesNotContain(alerts, a => a.Kind == AlertKind.TeacherPayDue); // pay day (30) not reached

        var full = Of(AlertKind.GroupFull);
        Assert.Equal(new InsightTarget(InsightLink.Course, s.GroupId, GroupId: s.GroupId), Assert.Single(full.Items).Target);
        Assert.Equal("2/2", full.Items[0].Value);
        Assert.DoesNotContain(alerts, a => a.Kind == AlertKind.GroupNearlyFull);

        Assert.Equal(InsightLink.Settings, Of(AlertKind.BackupOverdue).Target!.Link);
        Assert.Equal(new InsightTarget(InsightLink.Student, s.Chahra), Assert.Single(Of(AlertKind.StudentWithoutEnrollment).Items).Target);

        Assert.Equal(alerts.OrderByDescending(a => a.Severity).Select(a => a.Kind), alerts.Select(a => a.Kind));
        Assert.Equal(AlertKind.UnpaidStudents, alerts[0].Kind);

        var level = Assert.Single(insights.Levels);
        Assert.Equal(new LevelInsight("3AS", 3, 2), level);
    }

    [Fact]
    public async Task Alerts_follow_settings_and_dates()
    {
        await using var host = await TestHost.CreateAsync();
        await SeedAsync(host);
        var service = host.Get<IInsightsService>();
        var settings = host.Get<ISettingsService>();
        var s = await settings.GetAsync();
        s.LastBackupAt = Now.AddDays(-1);
        s.AbsenceAlertThreshold = 4;
        s.TeacherPayDay = 25;
        await settings.SaveAsync(s);

        var alerts = (await service.GetAsync(Now)).Alerts;
        Assert.DoesNotContain(alerts, a => a.Kind == AlertKind.BackupOverdue);
        Assert.DoesNotContain(alerts, a => a.Kind == AlertKind.AbsenceThreshold);
        var due = Assert.Single(alerts, a => a.Kind == AlertKind.TeacherPayDue);
        Assert.Equal(AlertSeverity.Warning, due.Severity);
        Assert.Equal(new DateTime(2026, 9, 1), due.Target!.Date);

        // Before the payment due day nothing is reported as unpaid; after the session ends only.
        var early = (await service.GetAsync(new DateTime(2026, 9, 5, 10, 0, 0))).Alerts;
        Assert.DoesNotContain(early, a => a.Kind == AlertKind.UnpaidStudents);
        var morning = (await service.GetAsync(Now.Date.AddHours(10))).Alerts;
        Assert.DoesNotContain(morning, a => a.Kind == AlertKind.AttendanceMissing);
    }

    [Fact]
    public async Task Empty_database_gives_zero_series_and_only_the_backup_alert()
    {
        await using var host = await TestHost.CreateAsync();
        var insights = await host.Get<IInsightsService>().GetAsync(Now, months: 1, weeks: 1);

        var month = Assert.Single(insights.Months);
        Assert.Equal(new DateTime(2026, 9, 1), month.Period);
        Assert.Equal(0, month.Revenue + month.Expenses + month.TeacherPay + month.Expected);
        Assert.Null(month.CollectionRate);
        Assert.Null(Assert.Single(insights.Weeks).Rate);
        Assert.Empty(insights.Levels);
        Assert.Equal(AlertKind.BackupOverdue, Assert.Single(insights.Alerts).Kind);
    }
}
