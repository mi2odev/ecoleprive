using CentreSoutien.Domain.Enums;

namespace CentreSoutien.Domain.Entities;

public class Parent : Entity
{
    public string FullName { get; set; } = "";
    public string? Relation { get; set; } = "Père";
    public string? Phone { get; set; }
    public string? Phone2 { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Profession { get; set; }
    public string? Notes { get; set; }

    public List<Student> Children { get; set; } = [];

    public override string ToString() => FullName;
}

public class Student : Entity
{
    /// <summary>Human-facing identifier, e.g. E1001.</summary>
    public string Matricule { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    public Gender Gender { get; set; }
    public string Level { get; set; } = "";
    public string? School { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? PhotoFile { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime EnrolledOn { get; set; } = DateTime.Today;
    public string? Notes { get; set; }

    public int? ParentId { get; set; }
    public Parent? Parent { get; set; }

    public int? DiscountId { get; set; }
    public Discount? Discount { get; set; }

    public List<Enrollment> Enrollments { get; set; } = [];
    public List<StudentPayment> Payments { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}".Trim();
    public string Initials => string.Concat(new[] { FirstName, LastName }.Where(s => s.Length > 0).Select(s => char.ToUpperInvariant(s[0])));

    public override string ToString() => FullName;
}

public class Teacher : Entity
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int StartYear { get; set; } = DateTime.Today.Year;
    public CompensationType CompensationType { get; set; } = CompensationType.Percentage;
    /// <summary>Percent (0–100), amount per session or monthly amount depending on <see cref="CompensationType"/>.</summary>
    public decimal CompensationValue { get; set; } = 40;
    public bool IsActive { get; set; } = true;
    public string? PhotoFile { get; set; }
    public string? Notes { get; set; }

    public List<Group> Groups { get; set; } = [];
    public List<TeacherPayment> Payments { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}".Trim();
    public string Initials => string.Concat(new[] { FirstName, LastName }.Where(s => s.Length > 0).Select(s => char.ToUpperInvariant(s[0])));

    public override string ToString() => FullName;
}
