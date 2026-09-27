namespace CentreSoutien.Domain.Enums;

public enum CompensationType
{
    /// <summary>Percentage of the monthly fees of the teacher's enrolled students.</summary>
    Percentage = 0,
    /// <summary>Fixed amount per session taught.</summary>
    PerSession = 1,
    /// <summary>Fixed monthly salary.</summary>
    FixedMonthly = 2,
}

public enum SessionStatus
{
    Planned = 0,
    Done = 1,
    Cancelled = 2,
}

public enum AttendanceStatus
{
    Present = 0,
    Absent = 1,
    Late = 2,
    Excused = 3,
}

public enum DiscountType
{
    Percent = 0,
    FixedAmount = 1,
}

public enum PaymentMethod
{
    Cash = 0,
    Ccp = 1,
    BaridiMob = 2,
    Cheque = 3,
    Transfer = 4,
}

public enum PaymentKind
{
    /// <summary>Payment for packs of sessions (was "Monthly" when groups were billed per month).</summary>
    Sessions = 0,
    Registration = 1,
    Other = 2,
}

public enum ExamType
{
    Test = 0,
    Homework = 1,
    Exam = 2,
}

public enum DocumentOwnerType
{
    Center = 0,
    Student = 1,
    Teacher = 2,
    Parent = 3,
}

public enum AppTheme
{
    Light = 0,
    Dark = 1,
}

public enum Gender
{
    Unspecified = 0,
    Male = 1,
    Female = 2,
}

public enum AuditCategory
{
    Payment = 0,
    TeacherPayment = 1,
    Student = 2,
    Group = 3,
    Expense = 4,
    Settings = 5,
    Security = 6,
    Backup = 7,
}
