using CentreSoutien.Domain.Enums;

namespace CentreSoutien.Domain.Entities;

public class Discount : Entity
{
    public string Name { get; set; } = "";
    public DiscountType Type { get; set; } = DiscountType.Percent;
    public decimal Value { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public decimal Apply(decimal amount) => Type == DiscountType.Percent
        ? Math.Max(0, amount - Math.Round(amount * Value / 100m, 0))
        : Math.Max(0, amount - Value);

    public string ValueLabel => Type == DiscountType.Percent ? $"−{Value:0.##} %" : "−" + Calculations.Money.Format(Value);

    public override string ToString() => $"{Name} ({ValueLabel})";
}

public class StudentPayment : Entity
{
    public string ReceiptNumber { get; set; } = "";
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentKind Kind { get; set; }
    /// <summary>Group the session payment is for (each group is paid on its own). Null for registration fees, other
    /// payments, and session payments recorded by older versions.</summary>
    public int? GroupId { get; set; }
    public Group? Group { get; set; }
    /// <summary>Month the payment was made (first day of the month).</summary>
    public DateTime Period { get; set; }
    public string? Note { get; set; }
}

public class TeacherPayment : Entity
{
    public int TeacherId { get; set; }
    public Teacher? Teacher { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    /// <summary>Month the payment covers (first day of the month).</summary>
    public DateTime Period { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Ccp;
    public string? Note { get; set; }
}

public class Expense : Entity
{
    public DateTime Date { get; set; } = DateTime.Today;
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Supplier { get; set; }
}

public class Document : Entity
{
    public string Title { get; set; } = "";
    public string? Category { get; set; }
    /// <summary>File name inside the application's document storage folder.</summary>
    public string StoredFile { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public long SizeBytes { get; set; }
    public DocumentOwnerType OwnerType { get; set; }
    public int? OwnerId { get; set; }
    public string? Notes { get; set; }
}
