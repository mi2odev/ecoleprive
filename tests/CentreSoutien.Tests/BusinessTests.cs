using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;

namespace CentreSoutien.Tests;

public class BusinessTests
{
    private static readonly DateTime Sept = new(2026, 9, 1);

    private static Group G(decimal price, string level = "3AS") =>
        new() { Id = Random.Shared.Next(1, 100000), Course = new Course { MonthlyPrice = price, Level = level, Subject = new Subject { Name = "Maths" } } };

    [Fact]
    public void Monthly_due_sums_enrollments_and_applies_discount()
    {
        var s = new Student { IsActive = true, Discount = new Discount { Type = DiscountType.Percent, Value = 10, IsActive = true } };
        s.Enrollments.Add(new Enrollment { Group = G(4500), StartDate = new(2026, 9, 10) });
        s.Enrollments.Add(new Enrollment { Group = G(3000), StartDate = new(2026, 8, 1), EndDate = new(2026, 8, 31) }); // ended
        Assert.Equal(4500, Billing.GrossMonthlyFee(s, Sept));
        Assert.Equal(4050, Billing.MonthlyDue(s, Sept));
        Assert.Equal(PaymentState.Unpaid, Billing.State(s, Sept));
        s.Payments.Add(new StudentPayment { Amount = 2000, Period = Sept, Kind = PaymentKind.Monthly });
        Assert.Equal(PaymentState.Partial, Billing.State(s, Sept));
        Assert.Equal(2050, Billing.Balance(s, Sept));
        s.Payments.Add(new StudentPayment { Amount = 2050, Period = Sept, Kind = PaymentKind.Monthly });
        Assert.Equal(PaymentState.Paid, Billing.State(s, Sept));
        s.IsActive = false;
        Assert.Equal(0, Billing.MonthlyDue(s, Sept));
    }

    [Fact]
    public void Teacher_earnings_follow_compensation_rules()
    {
        var g = G(4000);
        g.IsActive = true;
        g.Enrollments.AddRange([new Enrollment { StudentId = 1, StartDate = Sept }, new Enrollment { StudentId = 2, StartDate = Sept }]);
        g.Slots.Add(new ScheduleSlot { Day = DayOfWeek.Saturday, Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(11) });
        var pct = new Teacher { Id = 1, CompensationType = CompensationType.Percentage, CompensationValue = 40 };
        Assert.Equal(3200, TeacherEarnings.Compute(pct, [g], Sept, Money.Format).Amount);

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
        var courses = host.Get<ICourseService>();
        var groups = host.Get<IGroupService>();
        var teachers = host.Get<ITeacherService>();
        var studentsSvc = host.Get<IStudentService>();
        var payments = host.Get<IPaymentService>();

        var maths = await subjects.SaveAsync(new Subject { Name = "Mathématiques" });
        var room = await rooms.SaveAsync(new Room { Name = "Salle 1", Capacity = 12 });
        var t = await teachers.SaveAsync(new Teacher { FirstName = "Karima", LastName = "Boudiaf", SubjectId = maths.Id });
        var course = await courses.SaveAsync(new Course { SubjectId = maths.Id, Level = "3AS", MonthlyPrice = 4500 });
        var slot = new ScheduleSlot { Day = DayOfWeek.Saturday, Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(11) };
        var a = await groups.SaveAsync(new Group { CourseId = course.Id, Name = "A", TeacherId = t.Id, RoomId = room.Id, Capacity = 1 }, [slot]);

        // Same room, overlapping time → rejected.
        var ex = await Assert.ThrowsAsync<BusinessException>(() => groups.SaveAsync(
            new Group { CourseId = course.Id, Name = "B", RoomId = room.Id, Capacity = 5 },
            [new ScheduleSlot { Day = DayOfWeek.Saturday, Start = TimeSpan.FromHours(10), End = TimeSpan.FromHours(12) }]));
        Assert.Contains("Salle déjà occupée", ex.Message);
        var b = await groups.SaveAsync(new Group { CourseId = course.Id, Name = "B", Capacity = 5 },
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
        var p1 = await payments.RecordAsync(s1.Id, 4500, PaymentMethod.Cash, PaymentKind.Monthly, period, null);
        var p2 = await payments.RecordAsync(s2.Id, 1000, PaymentMethod.Ccp, PaymentKind.Monthly, period, null);
        Assert.Equal("REC-2026-0001", p1.ReceiptNumber);
        Assert.Equal("REC-2026-0002", p2.ReceiptNumber);

        var rows = await payments.MonthOverviewAsync(period);
        Assert.Equal(PaymentState.Paid, rows.Single(r => r.StudentId == s1.Id).State);
        Assert.Equal(3500, rows.Single(r => r.StudentId == s2.Id).Balance);

        var detail = await courses.GetDetailAsync(course.Id, period);
        Assert.Equal(9000, detail!.Expected);
        Assert.Equal(5500, detail.Collected);

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
