using CentreSoutien.Domain.Enums;

namespace CentreSoutien.Domain.Entities;

/// <summary>Single-row table holding every configurable setting of the center and application.</summary>
public class CenterSettings : Entity
{
    // Centre
    public string CenterName { get; set; } = "Future Leaders Academy";
    public string? LogoFile { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Currency { get; set; } = "DZD";
    public string AcademicYear { get; set; } = "2026–2027";

    // Pricing & payments
    public decimal RegistrationFee { get; set; } = 1000;
    public int PaymentDueDay { get; set; } = 5;
    public int ReminderAfterDays { get; set; } = 10;
    public decimal SiblingDiscountPercent { get; set; } = 10;
    public decimal QuarterlyDiscountPercent { get; set; } = 5;
    public string? PaymentRulesNote { get; set; }

    // Teacher compensation defaults
    public CompensationType DefaultCompensationType { get; set; } = CompensationType.Percentage;
    public decimal DefaultCompensationPercent { get; set; } = 40;
    public decimal DefaultSessionRate { get; set; } = 2000;
    public decimal DefaultMonthlySalary { get; set; } = 25000;
    public int TeacherPayDay { get; set; } = 30;

    // Attendance & grades
    public int LateAfterMinutes { get; set; } = 10;
    public int AbsenceAlertThreshold { get; set; } = 3;
    public bool NotifyParentOnAbsence { get; set; }
    public decimal GradeScale { get; set; } = 20;
    public decimal PassingGrade { get; set; } = 10;
    public int GradeDecimals { get; set; } = 2;

    // Receipts
    public string ReceiptPrefix { get; set; } = "REC-2026-";
    public int NextReceiptNumber { get; set; } = 1;
    public string? ReceiptFooter { get; set; } = "Merci de votre confiance.";
    public bool ShowLogoOnReceipt { get; set; } = true;

    // Backup
    public bool AutoBackupEnabled { get; set; } = true;
    public TimeSpan AutoBackupTime { get; set; } = new(22, 0, 0);
    public string? BackupFolder { get; set; }
    public int BackupRetentionCount { get; set; } = 30;
    public DateTime? LastBackupAt { get; set; }
    public long? LastBackupSize { get; set; }

    // Application
    public string Language { get; set; } = "fr";
    public AppTheme Theme { get; set; } = AppTheme.Light;

    // Security
    public int AutoLockMinutes { get; set; } = 10;
    /// <summary>Minutes the app can stay locked before the session ends and a full login is required.</summary>
    public int SessionTimeoutMinutes { get; set; } = 120;
}
