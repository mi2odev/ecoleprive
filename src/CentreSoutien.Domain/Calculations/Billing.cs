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
/// a pack of N sessions of the group has been held. Sessions are the group's sessions (not the student's attendance):
/// recorded sessions count unless cancelled, and days without recorded sessions follow the weekly timetable.
/// </summary>
public static class Packs
{
    /// <summary>Start times of the group's sessions held from <paramref name="from"/> until <paramref name="until"/> (inclusive) and before <paramref name="now"/>.</summary>
    public static List<DateTime> Held(Group g, DateTime from, DateTime? until, DateTime now)
    {
        var last = until is { } u && u.Date.AddDays(1) <= now ? u.Date.AddDays(1).AddTicks(-1) : now;
        var list = new List<DateTime>();
        if (last < from.Date) return list;
        var recorded = g.Sessions.Where(x => x.Date >= from.Date && x.Date <= last.Date).ToLookup(x => x.Date.Date);
        for (var d = from.Date; d <= last.Date; d = d.AddDays(1))
        {
            if (recorded.Contains(d))
                list.AddRange(recorded[d].Where(x => x.Status != SessionStatus.Cancelled).Select(x => x.StartsAt));
            else
                list.AddRange(g.Slots.Where(x => x.Day == d.DayOfWeek).Select(x => d + x.Start));
        }
        return list.Where(t => t <= last).Order().ToList();
    }

    public static bool Ended(Enrollment e, DateTime now) => e.EndDate is { } end && end.Date < now.Date;

    /// <summary>Packs billed for one enrollment up to <paramref name="now"/>. Needs Group with Slots and Sessions.</summary>
    public static List<Charge> Charges(Enrollment e, DateTime now, Discount? discount = null)
    {
        var g = e.Group;
        if (g is null || e.StartDate.Date > now.Date) return [];
        var size = Math.Max(1, g.SessionsPerPack);
        var held = Held(g, e.StartDate, e.EndDate, now);
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
        var held = e.StartDate.Date > now.Date ? 0 : Held(g, e.StartDate, e.EndDate, now).Count;
        return new PackStatus(g.Id, g.FullName, held / size + 1, held % size, size, Ended(e, now));
    }
}

/// <summary>Pure billing rules. Callers pass entity graphs with the needed navigation properties loaded
/// (student payments, discount, enrollments → group → subject, slots and sessions).</summary>
public static class Billing
{
    /// <summary>Every pack billed to the student so far, oldest first.</summary>
    public static List<Charge> Charges(Student student, DateTime now) =>
        student.Enrollments.SelectMany(e => Packs.Charges(e, now, student.Discount))
            .OrderBy(c => c.Date).ThenBy(c => c.GroupId).ThenBy(c => c.Pack).ToList();

    public static decimal Due(Student student, DateTime now) => Charges(student, now).Sum(c => c.Amount);

    /// <summary>Total of the student's session payments (registration fees and other payments excluded).</summary>
    public static decimal Paid(Student student) =>
        student.Payments.Where(x => x.Kind == PaymentKind.Sessions).Sum(x => x.Amount);

    public static decimal Balance(Student student, DateTime now) => Math.Max(0, Due(student, now) - Paid(student));

    /// <summary>Paid in advance (more than billed so far).</summary>
    public static decimal Credit(Student student, DateTime now) => Math.Max(0, Paid(student) - Due(student, now));

    /// <summary>Charges with the part paid, payments covering the oldest charges first.</summary>
    public static List<ChargeLine> Allocate(Student student, DateTime now)
    {
        var left = Paid(student);
        var lines = new List<ChargeLine>();
        foreach (var c in Charges(student, now))
        {
            var paid = Math.Min(left, c.Amount);
            left -= paid;
            lines.Add(new ChargeLine(c, paid));
        }
        return lines;
    }

    /// <summary>Price of one pack of each group the student currently attends (discount included).</summary>
    public static decimal PackPrice(Student student, DateTime now) =>
        student.Enrollments.Where(e => e.Group is not null && e.IsActiveOn(now))
            .Sum(e => student.Discount is { IsActive: true } d ? d.Apply(e.Group!.Price) : e.Group!.Price);

    public static List<PackStatus> Progress(Student student, DateTime now) =>
        student.Enrollments.Where(e => e.Group is not null && e.IsActiveOn(now)).Select(e => Packs.Status(e, now)).ToList();

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
