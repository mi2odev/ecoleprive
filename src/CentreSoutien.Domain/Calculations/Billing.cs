using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;

namespace CentreSoutien.Domain.Calculations;

public static class Period
{
    public static DateTime Of(DateTime date) => new(date.Year, date.Month, 1);
    public static DateTime Current => Of(DateTime.Today);
    public static DateTime End(DateTime period) => Of(period).AddMonths(1).AddDays(-1);
}

/// <summary>One pack of sessions billed to a student: due when the pack starts (enrollment, then every N sessions).</summary>
public sealed record Charge(int GroupId, string Group, int Pack, DateTime Date, decimal Amount, decimal Gross);

/// <summary>A charge and how much of it the student's payments cover (oldest charges are paid first).</summary>
public sealed record ChargeLine(Charge Charge, decimal Paid)
{
    public decimal Rest => Charge.Amount - Paid;
}

/// <summary>Where a student stands in a group: pack number and sessions already held in it ("séance 3/4").</summary>
public sealed record PackStatus(int GroupId, string Group, int Pack, int Done, int Size, bool Ended)
{
    public string Label => $"{Group} · séance {Math.Min(Done + 1, Size)}/{Size}";
}

/// <summary>
/// Groups are paid by packs of sessions (4, 8…): a student owes the group's price when joining, then again each time
/// a pack of N sessions of the group has been held. Recorded sessions count unless cancelled, and days without recorded
/// sessions follow the weekly timetable. When the group does not count absences (<see cref="Group.AbsencesCount"/>),
/// a session where the student was marked absent or excused is not counted for that student.
/// </summary>
public static class Packs
{
    /// <summary>Start times of the group's sessions held from <paramref name="from"/> until <paramref name="until"/> (inclusive) and before <paramref name="now"/>.</summary>
    /// <param name="studentId">Student whose absences are skipped when the group does not count them.</param>
    public static List<DateTime> Held(Group g, DateTime from, DateTime? until, DateTime now, int? studentId = null)
    {
        var last = until is { } u && u.Date.AddDays(1) <= now ? u.Date.AddDays(1).AddTicks(-1) : now;
        var list = new List<DateTime>();
        if (last < from.Date) return list;
        var recorded = g.Sessions.Where(x => x.Date >= from.Date && x.Date <= last.Date).ToLookup(x => x.Date.Date);
        var skipAbsences = !g.AbsencesCount && studentId is not null;
        for (var d = from.Date; d <= last.Date; d = d.AddDays(1))
        {
            if (recorded.Contains(d))
                list.AddRange(recorded[d].Where(x => x.Status != SessionStatus.Cancelled && !(skipAbsences && Missed(x, studentId!.Value))).Select(x => x.StartsAt));
            else
                list.AddRange(g.Slots.Where(x => x.Day == d.DayOfWeek).Select(x => d + x.Start));
        }
        return list.Where(t => t <= last).Order().ToList();
    }

    private static bool Missed(Session s, int studentId) =>
        s.Attendance.Any(a => a.StudentId == studentId && a.Status is AttendanceStatus.Absent or AttendanceStatus.Excused);

    public static bool Ended(Enrollment e, DateTime now) => e.EndDate is { } end && end.Date < now.Date;

    /// <summary>Packs billed for one enrollment up to <paramref name="now"/>. Needs Group with Slots and Sessions.</summary>
    public static List<Charge> Charges(Enrollment e, DateTime now, Discount? discount = null)
    {
        var g = e.Group;
        if (g is null || e.StartDate.Date > now.Date) return [];
        var size = Math.Max(1, g.SessionsPerPack);
        var held = Held(g, e.StartDate, e.EndDate, now, e.StudentId);
        // Still enrolled: a new pack starts as soon as the previous one is used up. Left: only the packs begun.
        var packs = Ended(e, now) ? (held.Count + size - 1) / size : 1 + held.Count / size;
        var amount = discount is { IsActive: true } d ? d.Apply(g.Price) : g.Price;
        return Enumerable.Range(1, packs)
            .Select(k => new Charge(g.Id, g.FullName, k, k == 1 ? e.StartDate.Date : held[(k - 1) * size - 1], amount, g.Price))
            .ToList();
    }

    public static PackStatus Status(Enrollment e, DateTime now)
    {
        var g = e.Group!;
        var size = Math.Max(1, g.SessionsPerPack);
        var held = e.StartDate.Date > now.Date ? 0 : Held(g, e.StartDate, e.EndDate, now, e.StudentId).Count;
        return new PackStatus(g.Id, g.FullName, held / size + 1, held % size, size, Ended(e, now));
    }
}

/// <summary>A student's account in one group: packs billed, paid for this group, left to pay, paid in advance.</summary>
public sealed record GroupAccount(int GroupId, string Group, PackStatus? Status, decimal PackPrice, decimal Due, decimal Paid,
    decimal Balance, decimal Credit, DateTime? DueSince, PaymentState State);

/// <summary>
/// Pure billing rules. Callers pass entity graphs with the needed navigation properties loaded
/// (student payments, discount, enrollments → group → subject, slots and sessions).
/// Each group is paid on its own: a payment made for a group only covers that group's packs. Session payments recorded
/// without a group (older versions) cover the oldest unpaid packs of any group.
/// </summary>
public static class Billing
{
    /// <summary>Every pack billed to the student so far, oldest first.</summary>
    public static List<Charge> Charges(Student student, DateTime now) =>
        student.Enrollments.SelectMany(e => Packs.Charges(e, now, student.Discount))
            .OrderBy(c => c.Date).ThenBy(c => c.GroupId).ThenBy(c => c.Pack).ToList();

    public static decimal Due(Student student, DateTime now) => Charges(student, now).Sum(c => c.Amount);

    private static IEnumerable<StudentPayment> SessionPayments(Student student) => student.Payments.Where(x => x.Kind == PaymentKind.Sessions && !x.IsCancelled);

    /// <summary>Total of the student's session payments (registration fees and other payments excluded).</summary>
    public static decimal Paid(Student student) => SessionPayments(student).Sum(x => x.Amount);

    /// <summary>Session payments made for <paramref name="groupId"/>.</summary>
    public static decimal PaidFor(Student student, int groupId) => SessionPayments(student).Where(x => x.GroupId == groupId).Sum(x => x.Amount);

    /// <summary>
    /// Charges with the part paid. Each group's payments cover that group's packs, oldest first; payments without a
    /// group then cover the oldest packs still open.
    /// </summary>
    public static List<ChargeLine> Allocate(Student student, DateTime now)
    {
        var charges = Charges(student, now);
        var paid = new decimal[charges.Count];
        foreach (var group in charges.Select((c, i) => (c, i)).GroupBy(x => x.c.GroupId))
        {
            var left = PaidFor(student, group.Key);
            foreach (var (c, i) in group)
            {
                paid[i] = Math.Min(left, c.Amount);
                left -= paid[i];
            }
        }
        var pool = SessionPayments(student).Where(x => x.GroupId is null).Sum(x => x.Amount);
        for (var i = 0; i < charges.Count && pool > 0; i++)
        {
            var more = Math.Min(pool, charges[i].Amount - paid[i]);
            paid[i] += more;
            pool -= more;
        }
        return charges.Select((c, i) => new ChargeLine(c, paid[i])).ToList();
    }

    /// <summary>Left to pay, all groups together (a group paid in advance does not reduce another group's debt).</summary>
    public static decimal Balance(Student student, DateTime now) => Allocate(student, now).Sum(l => l.Rest);

    /// <summary>Paid in advance (more than billed so far), all groups together.</summary>
    public static decimal Credit(Student student, DateTime now) => Math.Max(0, Paid(student) - Allocate(student, now).Sum(l => l.Paid));

    /// <summary>Price of one pack of each group the student currently attends (discount included).</summary>
    public static decimal PackPrice(Student student, DateTime now) =>
        student.Enrollments.Where(e => e.Group is not null && e.IsActiveOn(now)).Sum(e => PackPrice(student, e.Group!));

    public static decimal PackPrice(Student student, Group g) => student.Discount is { IsActive: true } d ? d.Apply(g.Price) : g.Price;

    public static List<PackStatus> Progress(Student student, DateTime now) =>
        student.Enrollments.Where(e => e.Group is not null && e.IsActiveOn(now)).Select(e => Packs.Status(e, now)).ToList();

    /// <summary>
    /// One account per group: the groups the student attends now, plus older groups still owing or paid in advance.
    /// </summary>
    public static List<GroupAccount> Accounts(Student student, DateTime now)
    {
        var lines = Allocate(student, now);
        return student.Enrollments.Where(e => e.Group is not null).GroupBy(e => e.GroupId).Select(x =>
        {
            var g = x.First().Group!;
            var current = x.FirstOrDefault(e => e.IsActiveOn(now));
            var mine = lines.Where(l => l.Charge.GroupId == g.Id).ToList();
            var due = mine.Sum(l => l.Charge.Amount);
            var covered = mine.Sum(l => l.Paid);
            var credit = Math.Max(0, PaidFor(student, g.Id) - covered);
            var open = mine.FirstOrDefault(l => l.Rest > 0);
            var state = !student.IsActive ? PaymentState.Inactive
                : open is null ? PaymentState.Paid
                : open.Paid > 0 ? PaymentState.Partial : PaymentState.Unpaid;
            return new GroupAccount(g.Id, g.FullName, current is null ? null : Packs.Status(current, now), PackPrice(student, g),
                due, covered, due - covered, credit, open?.Charge.Date, state);
        })
        .Where(a => a.Status is not null || a.Balance > 0 || a.Credit > 0)
        .OrderBy(a => a.Group).ToList();
    }

    public static PaymentState State(Student student, DateTime now)
    {
        if (!student.IsActive) return PaymentState.Inactive;
        var open = Allocate(student, now).FirstOrDefault(l => l.Rest > 0);
        if (open is null) return PaymentState.Paid;
        return open.Paid > 0 ? PaymentState.Partial : PaymentState.Unpaid;
    }
}

public enum PaymentState
{
    Paid,
    Partial,
    Unpaid,
    Inactive,
}

public sealed record EarningsResult(decimal Amount, string Rule, int Basis);

/// <summary>Teacher compensation rules.</summary>
public static class TeacherEarnings
{
    /// <summary>
    /// Earnings for the month. Groups must have Course, Enrollments, Slots and Sessions loaded.
    /// Per-session pay counts the non-cancelled sessions of the month; when no session has been
    /// generated yet for the month it falls back to the number of timetable occurrences.
    /// </summary>
    /// <remarks>Percentage pay: share of the packs of sessions that started during the month (list price, before
    /// discounts), counted up to <paramref name="now"/> (default: the whole month).</remarks>
    public static EarningsResult Compute(Teacher teacher, IEnumerable<Group> teacherGroups, DateTime period, Func<decimal, string> money, DateTime? now = null)
    {
        var p = Period.Of(period);
        var end = Period.End(p).AddDays(1).AddTicks(-1);
        var until = now is { } n && n < end ? n : end;
        var groups = teacherGroups.Where(g => g.IsActive).ToList();
        switch (teacher.CompensationType)
        {
            case CompensationType.Percentage:
            {
                var packs = groups.SelectMany(g => g.Enrollments.Select(e =>
                    {
                        e.Group ??= g;
                        return (e.StudentId, Charges: Packs.Charges(e, until).Where(c => c.Date >= p).ToList());
                    }))
                    .Where(x => x.Charges.Count > 0).ToList();
                var baseAmount = packs.Sum(x => x.Charges.Sum(c => c.Gross));
                var students = packs.Select(x => x.StudentId).Distinct().Count();
                return new(Math.Round(baseAmount * teacher.CompensationValue / 100m, 0),
                    $"{teacher.CompensationValue:0.##} % des séances de {students} élève{(students > 1 ? "s" : "")}", students);
            }
            case CompensationType.PerSession:
            {
                var count = CountSessions(teacher, groups, p);
                return new(count * teacher.CompensationValue, $"{count} séance{(count > 1 ? "s" : "")} × {money(teacher.CompensationValue)}", count);
            }
            default:
                return new(teacher.CompensationValue, "Forfait mensuel", 0);
        }
    }

    public static int CountSessions(Teacher teacher, IEnumerable<Group> groups, DateTime period)
    {
        var p = Period.Of(period);
        var end = Period.End(p);
        var list = groups.ToList();
        var sessions = list.SelectMany(g => g.Sessions)
            .Where(s => s.Date >= p && s.Date <= end && s.Status != SessionStatus.Cancelled && (s.TeacherId ?? s.Group?.TeacherId ?? teacher.Id) == teacher.Id)
            .ToList();
        if (list.SelectMany(g => g.Sessions).Any(s => s.Date >= p && s.Date <= end))
            return sessions.Count;
        // Projection from the weekly timetable.
        var count = 0;
        for (var d = p; d <= end; d = d.AddDays(1))
            count += list.SelectMany(g => g.Slots).Count(s => s.Day == d.DayOfWeek);
        return count;
    }

    public static string RuleLabel(Teacher t, Func<decimal, string> money) => t.CompensationType switch
    {
        CompensationType.Percentage => $"{t.CompensationValue:0.##} % des séances payées par les élèves",
        CompensationType.PerSession => $"{money(t.CompensationValue)} / séance",
        _ => $"{money(t.CompensationValue)} / mois",
    };
}

public static class Grading
{
    /// <summary>Weighted average normalised to the grading scale (e.g. /20). Returns null when no scores exist.</summary>
    public static decimal? Average(IEnumerable<Grade> grades, decimal scale, int decimals = 2)
    {
        var list = grades.Where(g => g.Score.HasValue && g.Exam is not null && g.Exam.MaxScore > 0).ToList();
        if (list.Count == 0) return null;
        var totalCoef = list.Sum(g => g.Exam!.Coefficient);
        if (totalCoef <= 0) return null;
        var sum = list.Sum(g => g.Score!.Value / g.Exam!.MaxScore * scale * g.Exam.Coefficient);
        return Math.Round(sum / totalCoef, decimals);
    }
}
