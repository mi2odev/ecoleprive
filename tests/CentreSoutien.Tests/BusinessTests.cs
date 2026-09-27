using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;

namespace CentreSoutien.Tests;

public class BusinessTests
{
    private static readonly DateTime Sept = new(2026, 9, 1);

    private static Group G(decimal price, string level = "3AS", int pack = 4) =>
        new() { Id = Random.Shared.Next(1, 100000), Price = price, SessionsPerPack = pack, Level = level, Subject = new Subject { Name = "Maths" } };

    /// <summary>Group meeting every Saturday 9:00–11:00 (September 2026: 5, 12, 19, 26).</summary>
    private static Group Saturdays(decimal price, int pack = 4)
    {
        var g = G(price, pack: pack);
        g.Slots.Add(new ScheduleSlot { Day = DayOfWeek.Saturday, Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(11) });
        return g;
    }

    [Fact]
    public void A_pack_is_due_on_joining_then_every_N_sessions()
    {
        var g = Saturdays(4000);
        var s = new Student { IsActive = true, Discount = new Discount { Type = DiscountType.Percent, Value = 10, IsActive = true } };
        var e = new Enrollment { Group = g, StartDate = new(2026, 9, 5) };
        s.Enrollments.Add(e);

        // Joining: the first pack (discount included) is due right away.
        var join = new DateTime(2026, 9, 5, 8, 0, 0);
        Assert.Equal(3600, Billing.Due(s, join));
        Assert.Equal(new DateTime(2026, 9, 5), Billing.Charges(s, join).Single().Date);
        Assert.Equal(PaymentState.Unpaid, Billing.State(s, join));

        // 3 sessions held: still the first pack, on its 4th and last session.
        var beforeFourth = new DateTime(2026, 9, 26, 8, 0, 0);
        Assert.Single(Billing.Charges(s, beforeFourth));
        Assert.Equal("Maths · 3AS A · séance 4/4", Packs.Status(e, beforeFourth).Label);

        // The 4th session is held: the pack is used up and the next one is due.
        var afterFourth = new DateTime(2026, 9, 26, 12, 0, 0);
        var charges = Billing.Charges(s, afterFourth);
        Assert.Equal(2, charges.Count);
        Assert.Equal(new DateTime(2026, 9, 26, 9, 0, 0), charges[1].Date);
        Assert.Equal("Maths · 3AS A · séance 1/4", Packs.Status(e, afterFourth).Label);

        // Payments cover the oldest pack first.
        s.Payments.Add(new StudentPayment { Amount = 3600, Kind = PaymentKind.Sessions });
        Assert.Equal(PaymentState.Unpaid, Billing.State(s, afterFourth));
        s.Payments.Add(new StudentPayment { Amount = 1000, Kind = PaymentKind.Sessions });
        s.Payments.Add(new StudentPayment { Amount = 5000, Kind = PaymentKind.Registration }); // not a session payment
        Assert.Equal(PaymentState.Partial, Billing.State(s, afterFourth));
        Assert.Equal(2600, Billing.Balance(s, afterFourth));
        s.Payments.Add(new StudentPayment { Amount = 3000, Kind = PaymentKind.Sessions });
        Assert.Equal(PaymentState.Paid, Billing.State(s, afterFourth));
        Assert.Equal(400, Billing.Credit(s, afterFourth)); // paid in advance
        Assert.Equal(3600, Billing.PackPrice(s, afterFourth));

        s.IsActive = false;
        Assert.Equal(PaymentState.Inactive, Billing.State(s, afterFourth));
    }

    [Fact]
    public void A_group_can_leave_absences_out_of_the_paid_sessions()
    {
        var g = Saturdays(4000);
        g.Sessions.Add(new Session
        {
            Date = new(2026, 9, 12), Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(11), Status = SessionStatus.Done,
            Attendance = [new AttendanceRecord { StudentId = 7, Status = AttendanceStatus.Absent }, new AttendanceRecord { StudentId = 8, Status = AttendanceStatus.Present }],
        });
        var absent = new Enrollment { Group = g, StudentId = 7, StartDate = new(2026, 9, 5) };
        var present = new Enrollment { Group = g, StudentId = 8, StartDate = new(2026, 9, 5) };
        var now = new DateTime(2026, 9, 26, 12, 0, 0); // 5, 12, 19, 26 held

        // Absences count (default): both students have used their 4 sessions and owe a second pack.
        Assert.Equal(2, Packs.Charges(absent, now).Count);
        Assert.Equal(2, Packs.Charges(present, now).Count);

        // Absences not counted: the absent student is only at 3 sessions (séance 4/4 next), the other is unchanged.
        g.AbsencesCount = false;
        Assert.Single(Packs.Charges(absent, now));
        Assert.Equal("Maths · 3AS A · séance 4/4", Packs.Status(absent, now).Label);
        Assert.Equal(2, Packs.Charges(present, now).Count);
    }

    [Fact]
    public void Each_group_is_paid_on_its_own()
    {
        var maths = Saturdays(4000);
        var physics = G(3000);
        physics.Slots.Add(new ScheduleSlot { Day = DayOfWeek.Sunday, Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(11) });
        var s = new Student { IsActive = true };
        s.Enrollments.Add(new Enrollment { Group = maths, GroupId = maths.Id, StartDate = new(2026, 9, 5) });
        s.Enrollments.Add(new Enrollment { Group = physics, GroupId = physics.Id, StartDate = new(2026, 9, 5) });
        var now = new DateTime(2026, 9, 5, 12, 0, 0);

        // 5 000 paid for maths: its 4 000 is covered, 1 000 in advance for maths only — physics still owes 3 000.
        s.Payments.Add(new StudentPayment { Amount = 5000, Kind = PaymentKind.Sessions, GroupId = maths.Id });
        var accounts = Billing.Accounts(s, now);
        var m = accounts.Single(a => a.GroupId == maths.Id);
        var p = accounts.Single(a => a.GroupId == physics.Id);
        Assert.Equal((0m, 1000m, PaymentState.Paid), (m.Balance, m.Credit, m.State));
        Assert.Equal((3000m, 0m, PaymentState.Unpaid), (p.Balance, p.Credit, p.State));
        Assert.Equal(3000, Billing.Balance(s, now));
        Assert.Equal(1000, Billing.Credit(s, now));
        Assert.Equal(PaymentState.Unpaid, Billing.State(s, now));

        // A payment without a group (older versions) covers the oldest unpaid packs of any group.
        s.Payments.Add(new StudentPayment { Amount = 3000, Kind = PaymentKind.Sessions });
        Assert.Equal(0, Billing.Balance(s, now));
        Assert.Equal(PaymentState.Paid, Billing.Accounts(s, now).Single(a => a.GroupId == physics.Id).State);
    }

    [Fact]
    public void Cancelled_sessions_do_not_count_and_leaving_bills_only_the_packs_begun()
    {
        var g = Saturdays(4000);
        g.Sessions.Add(new Session { Date = new(2026, 9, 12), Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(11), Status = SessionStatus.Cancelled });
        var s = new Student { IsActive = true };
        var e = new Enrollment { Group = g, StartDate = new(2026, 9, 5) };
        s.Enrollments.Add(e);
        var now = new DateTime(2026, 9, 26, 12, 0, 0);
        Assert.Equal(3, Packs.Held(g, e.StartDate, null, now).Count); // 5, 19, 26 (12 cancelled)
        Assert.Single(Billing.Charges(s, now));

        // Left after 5 sessions of packs of 4: the second pack was begun, nothing more.
        var eight = Saturdays(4000);
        var leaver = new Student { IsActive = true };
        leaver.Enrollments.Add(new Enrollment { Group = eight, StartDate = new(2026, 9, 5), EndDate = new(2026, 10, 4) });
        Assert.Equal(8000, Billing.Due(leaver, new DateTime(2026, 12, 1)));

        // Packs of 8: one pack for the whole month.
        var big = new Student { IsActive = true };
        big.Enrollments.Add(new Enrollment { Group = Saturdays(8000, pack: 8), StartDate = new(2026, 9, 5) });
        Assert.Equal(8000, Billing.Due(big, now));
    }

    [Fact]
    public void Teacher_earnings_follow_compensation_rules()
    {
        var g = G(4000);
        g.IsActive = true;
        g.Enrollments.AddRange([new Enrollment { StudentId = 1, StartDate = Sept }, new Enrollment { StudentId = 2, StartDate = Sept }]);
        g.Slots.Add(new ScheduleSlot { Day = DayOfWeek.Saturday, Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(11) });
        var pct = new Teacher { Id = 1, CompensationType = CompensationType.Percentage, CompensationValue = 40 };
        // Two students, packs of 4 Saturdays: a pack each on joining (1st), a second after the 4th Saturday (26th).
        Assert.Equal(3200, TeacherEarnings.Compute(pct, [g], Sept, Money.Format, new DateTime(2026, 9, 20)).Amount);
        Assert.Equal(6400, TeacherEarnings.Compute(pct, [g], Sept, Money.Format).Amount);

        var per = new Teacher { Id = 1, CompensationType = CompensationType.PerSession, CompensationValue = 2000 };
        // September 2026 has 4 Saturdays; no generated sessions → timetable projection.
        Assert.Equal(8000, TeacherEarnings.Compute(per, [g], Sept, Money.Format).Amount);
        g.Sessions.Add(new Session { Date = new(2026, 9, 5), TeacherId = 1, Status = SessionStatus.Done });
        g.Sessions.Add(new Session { Date = new(2026, 9, 12), TeacherId = 1, Status = SessionStatus.Cancelled });
        Assert.Equal(2000, TeacherEarnings.Compute(per, [g], Sept, Money.Format).Amount);

        var fixe = new Teacher { CompensationType = CompensationType.FixedMonthly, CompensationValue = 25000 };
        Assert.Equal(25000, TeacherEarnings.Compute(fixe, [g], Sept, Money.Format).Amount);
    }

    [Fact]
    public void Money_is_formatted_with_spaces()
    {
        Assert.Equal("1 234 500 DZD", Money.Format(1234500));
    }

    [Fact]
    public async Task Owner_can_manage_course_group_enrollment_and_payments_end_to_end()
    {
        await using var host = await TestHost.CreateAsync();
        var subjects = host.Get<ICrudService<Subject>>();
        var rooms = host.Get<ICrudService<Room>>();
        var groups = host.Get<IGroupService>();
        var teachers = host.Get<ITeacherService>();
        var studentsSvc = host.Get<IStudentService>();
        var payments = host.Get<IPaymentService>();

        var maths = await subjects.SaveAsync(new Subject { Name = "Mathématiques" });
        var room = await rooms.SaveAsync(new Room { Name = "Salle 1", Capacity = 12 });
        var t = await teachers.SaveAsync(new Teacher { FirstName = "Karima", LastName = "Boudiaf", SubjectId = maths.Id });
        var slot = new ScheduleSlot { Day = DayOfWeek.Saturday, Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(11) };
        var a = await groups.SaveAsync(new Group { SubjectId = maths.Id, Level = "3AS", Price = 4500, Name = "A", TeacherId = t.Id, RoomId = room.Id, Capacity = 1 }, [slot]);

        // Same room, overlapping time → rejected.
        var ex = await Assert.ThrowsAsync<BusinessException>(() => groups.SaveAsync(
            new Group { SubjectId = maths.Id, Level = "3AS", Price = 4500, Name = "B", RoomId = room.Id, Capacity = 5 },
            [new ScheduleSlot { Day = DayOfWeek.Saturday, Start = TimeSpan.FromHours(10), End = TimeSpan.FromHours(12) }]));
        Assert.Contains("Salle déjà occupée", ex.Message);
        var b = await groups.SaveAsync(new Group { SubjectId = maths.Id, Level = "3AS", Price = 4500, Name = "B", Capacity = 5 },
            [new ScheduleSlot { Day = DayOfWeek.Sunday, Start = TimeSpan.FromHours(10), End = TimeSpan.FromHours(12) }]);

        var s1 = await studentsSvc.SaveAsync(new Student { FirstName = "Yacine", LastName = "Benali", Level = "3AS" });
        var s2 = await studentsSvc.SaveAsync(new Student { FirstName = "Amira", LastName = "Haddad", Level = "3AS" });
        Assert.Equal("E1001", s1.Matricule);
        Assert.Equal("E1002", s2.Matricule);

        await studentsSvc.EnrollAsync(s1.Id, a.Id);
        await Assert.ThrowsAsync<BusinessException>(() => studentsSvc.EnrollAsync(s2.Id, a.Id)); // full
        await studentsSvc.ChangeGroupAsync(s1.Id, a.Id, b.Id);
        await studentsSvc.EnrollAsync(s2.Id, a.Id);

        var period = new DateTime(2026, 9, 1);
        await Assert.ThrowsAsync<BusinessException>(() => payments.RecordAsync(s1.Id, null, 4500, PaymentMethod.Cash, PaymentKind.Sessions, null)); // which group?
        await Assert.ThrowsAsync<BusinessException>(() => payments.RecordAsync(s1.Id, a.Id, 4500, PaymentMethod.Cash, PaymentKind.Sessions, null)); // left it
        var p1 = await payments.RecordAsync(s1.Id, b.Id, 4500, PaymentMethod.Cash, PaymentKind.Sessions, null);
        var p2 = await payments.RecordAsync(s2.Id, a.Id, 1000, PaymentMethod.Ccp, PaymentKind.Sessions, null);
        Assert.Equal("REC-2026-0001", p1.ReceiptNumber);
        Assert.Equal("REC-2026-0002", p2.ReceiptNumber);

        var rows = await payments.OverviewAsync(); // joined today: first pack of 4 sessions due
        Assert.Equal(PaymentState.Paid, rows.Single(r => r.StudentId == s1.Id).State);
        Assert.Equal(3500, rows.Single(r => r.StudentId == s2.Id).Balance);

        var groupA = await groups.GetDetailAsync(a.Id, period);
        Assert.Equal(4500, groupA!.Expected); // Amira
        Assert.Equal(1000, groupA.Collected);
        var groupB = await groups.GetDetailAsync(b.Id, period);
        Assert.Equal(4500, groupB!.Expected); // Yacine, paid in full
        Assert.Equal(4500, groupB.Collected);

        await Assert.ThrowsAsync<BusinessException>(() => studentsSvc.DeleteAsync(s1.Id)); // has payments
    }

    [Fact]
    public async Task Sessions_attendance_and_grades_work_together()
    {
        await using var host = await TestHost.CreateAsync();
        await host.Get<IDemoDataService>().SeedAsync();
        var sessions = host.Get<ISessionService>();
        var today = new DateTime(2026, 9, 26);
        // Demo data already holds this week's sessions; generate the next week from the timetable.
        Assert.Equal(0, await sessions.GenerateAsync(today, today.AddDays(5)));
        Assert.True(await sessions.GenerateAsync(today.AddDays(7), today.AddDays(13)) > 0);
        Assert.Equal(0, await sessions.GenerateAsync(today.AddDays(7), today.AddDays(13))); // idempotent

        var first = (await sessions.ListAsync(today, today)).First(s => s.Group!.Enrollments.Count > 0);
        var att = host.Get<IAttendanceService>();
        var sheet = await att.GetSheetAsync(first.Id);
        Assert.NotEmpty(sheet.Lines);
        await att.SaveAsync(first.Id, sheet.Lines.ToDictionary(l => l.StudentId, _ => AttendanceStatus.Present));
        var counts = await att.CountsForDayAsync(today);
        Assert.True(counts.Present >= sheet.Lines.Count);

        var exams = host.Get<IExamService>();
        var exam = await exams.SaveAsync(new Exam { GroupId = first.GroupId, Title = "Composition", Date = today, MaxScore = 20 });
        var gs = await exams.GetGradeSheetAsync(exam.Id);
        await Assert.ThrowsAsync<BusinessException>(() => exams.SaveGradesAsync(exam.Id, new Dictionary<int, decimal?> { [gs.Lines[0].StudentId] = 25 }));
        await exams.SaveGradesAsync(exam.Id, gs.Lines.ToDictionary(l => l.StudentId, _ => (decimal?)14));
        var grades = await host.Get<IStudentService>().GradesAsync(gs.Lines[0].StudentId);
        Assert.Contains(grades, g => g.GroupId == first.GroupId);
    }

    [Fact]
    public async Task Demo_data_feeds_dashboard_reports_and_export()
    {
        await using var host = await TestHost.CreateAsync();
        var demo = host.Get<IDemoDataService>();
        Assert.True(await demo.IsDatabaseEmptyAsync());
        await demo.SeedAsync();
        Assert.False(await demo.IsDatabaseEmptyAsync());

        var now = new DateTime(2026, 9, 26, 14, 32, 0);
        var d = await host.Get<IDashboardService>().GetAsync(now);
        Assert.Equal(50, d.TotalStudents);
        Assert.Equal(9, d.TotalTeachers);
        Assert.Equal(8, d.ActiveTeachers);
        Assert.NotEmpty(d.TodaySessions);
        Assert.NotEmpty(d.Rooms);
        Assert.True(d.RevenueMonth > 0);
        Assert.True(d.AttendanceToday.Total > 0);

        var report = await host.Get<IReportService>().FinanceAsync(now);
        Assert.True(report.Expected > 0 && report.Collected > 0 && report.Outstanding > 0);
        Assert.NotEmpty(report.ByCourse);
        Assert.NotEmpty(await host.Get<IReportService>().AttendanceAsync(now.AddDays(-7), now));
        Assert.NotEmpty(await host.Get<IReportService>().GradesAsync());

        Assert.Equal(50, (await host.Get<IStudentService>().ListAsync(now)).Count);
        Assert.Equal(9, (await host.Get<ITeacherService>().ListAsync(now)).Count);
        var detail = await host.Get<ITeacherService>().GetDetailAsync((await host.Get<ITeacherService>().ListAsync(now))[0].Id, now);
        Assert.NotNull(detail);
        Assert.NotEmpty(await host.Get<IScheduleService>().WeekAsync());

        var xlsx = Path.Combine(host.Folder, "export.xlsx");
        await host.Get<IExportService>().ExportAllAsync(xlsx);
        Assert.True(new FileInfo(xlsx).Length > 5000);
    }
}
