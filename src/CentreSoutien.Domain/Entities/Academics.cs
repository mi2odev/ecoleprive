using CentreSoutien.Domain.Enums;

namespace CentreSoutien.Domain.Entities;

public class Subject : Entity
{
    public string Name { get; set; } = "";
    public string? ShortName { get; set; }
    public string? Description { get; set; }

    public string Display => string.IsNullOrWhiteSpace(ShortName) ? Name : ShortName;
    public override string ToString() => Name;
}

public class Room : Entity
{
    public string Name { get; set; } = "";
    public int Capacity { get; set; } = 16;
    public string? Equipment { get; set; }
    public bool IsActive { get; set; } = true;

    public override string ToString() => Name;
}

/// <summary>
/// A class: a subject taught at a level, with its price per pack of sessions, teacher, room, capacity, timetable and
/// students, e.g. "Mathématiques · 3AS A" at 4 500 DZD for 4 sessions. (Older versions had a separate "course" level.)
/// </summary>
public class Group : Entity
{
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public string Level { get; set; } = "";
    /// <summary>Price of one pack of <see cref="SessionsPerPack"/> sessions.</summary>
    public decimal Price { get; set; }
    /// <summary>Sessions paid at once (4, 8…): students pay when they join, then every N sessions.</summary>
    public int SessionsPerPack { get; set; } = 4;
    /// <summary>
    /// True (default): every session of the group counts in the student's pack, even when absent. False: a session
    /// where the student is marked absent or excused is not counted for them (their pack lasts longer).
    /// </summary>
    public bool AbsencesCount { get; set; } = true;
    public string Name { get; set; } = "A";
    public int? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public int? RoomId { get; set; }
    public Room? Room { get; set; }
    public int Capacity { get; set; } = 12;
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }

    public List<ScheduleSlot> Slots { get; set; } = [];
    public List<Enrollment> Enrollments { get; set; } = [];
    public List<Session> Sessions { get; set; } = [];

    /// <summary>"Mathématiques · 3AS" — subject and level without the group letter.</summary>
    public string SubjectLevel => $"{Subject?.Name ?? "?"} · {Level}";
    public string FullName => $"{SubjectLevel} {Name}".TrimEnd();
    /// <summary>"4 500 DZD / 4 séances".</summary>
    public string PriceLabel => $"{Calculations.Money.Format(Price)} / {SessionsPerPack} séance{(SessionsPerPack > 1 ? "s" : "")}";
    public override string ToString() => FullName;
}

/// <summary>Weekly recurring time slot of a group.</summary>
public class ScheduleSlot : Entity
{
    public int GroupId { get; set; }
    public Group? Group { get; set; }
    public DayOfWeek Day { get; set; }
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    /// <summary>Optional room override for this slot; defaults to the group's room.</summary>
    public int? RoomId { get; set; }
    public Room? Room { get; set; }

    public bool Overlaps(ScheduleSlot other) => Day == other.Day && Start < other.End && other.Start < End;
}

public class Enrollment : Entity
{
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public int GroupId { get; set; }
    public Group? Group { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }

    public bool IsActiveOn(DateTime date) => StartDate.Date <= date.Date && (EndDate is null || EndDate.Value.Date >= date.Date);

    /// <summary>True when the enrollment covers at least one day of the given month.</summary>
    public bool CoversMonth(int year, int month)
    {
        var first = new DateTime(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        return StartDate.Date <= last && (EndDate is null || EndDate.Value.Date >= first);
    }
}

/// <summary>A concrete class meeting on a date (generated from the timetable or created ad hoc).</summary>
public class Session : Entity
{
    public int GroupId { get; set; }
    public Group? Group { get; set; }
    public DateTime Date { get; set; }
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public int? RoomId { get; set; }
    public Room? Room { get; set; }
    public int? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public SessionStatus Status { get; set; }
    public string? Topic { get; set; }

    public List<AttendanceRecord> Attendance { get; set; } = [];

    public DateTime StartsAt => Date.Date + Start;
    public DateTime EndsAt => Date.Date + End;
}

public class AttendanceRecord : Entity
{
    public int SessionId { get; set; }
    public Session? Session { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public AttendanceStatus Status { get; set; }
    public string? Note { get; set; }
}

public class Exam : Entity
{
    public int GroupId { get; set; }
    public Group? Group { get; set; }
    public string Title { get; set; } = "";
    public ExamType Type { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public decimal MaxScore { get; set; } = 20;
    public decimal Coefficient { get; set; } = 1;
    public string? Notes { get; set; }

    public List<Grade> Grades { get; set; } = [];
}

public class Grade : Entity
{
    public int ExamId { get; set; }
    public Exam? Exam { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public decimal? Score { get; set; }
    public string? Comment { get; set; }
}
