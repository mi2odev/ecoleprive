using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;

namespace CentreSoutien.Application.Abstractions;

// Every screen talks to these interfaces only. The local implementation uses EF Core + SQLite;
// a future multi-computer version can provide HTTP-backed implementations without touching the UI.

/// <summary>Simple create/read/update/delete for reference data (subjects, rooms, discounts, expenses…).</summary>
public interface ICrudService<T> where T : Entity
{
    Task<List<T>> ListAsync(CancellationToken ct = default);
    Task<T?> GetAsync(int id, CancellationToken ct = default);
    Task<T> SaveAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public interface ISettingsService
{
    Task<CenterSettings> GetAsync(CancellationToken ct = default);
    Task<CenterSettings> SaveAsync(CenterSettings settings, CancellationToken ct = default);
}

public interface IStudentService
{
    Task<List<StudentListItem>> ListAsync(DateTime period, CancellationToken ct = default);
    /// <summary>Full graph: parent, discount, enrollments (group → course → subject, teacher, slots), payments.</summary>
    Task<Student?> GetAsync(int id, CancellationToken ct = default);
    Task<Student> SaveAsync(Student student, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<string> NextMatriculeAsync(CancellationToken ct = default);
    Task EnrollAsync(int studentId, int groupId, CancellationToken ct = default);
    /// <summary>Ends the student's enrollment in the group (kept for history).</summary>
    Task UnenrollAsync(int studentId, int groupId, CancellationToken ct = default);
    Task ChangeGroupAsync(int studentId, int fromGroupId, int toGroupId, CancellationToken ct = default);
    Task SetDiscountAsync(int studentId, int? discountId, CancellationToken ct = default);
    Task<List<StudentAttendanceItem>> AttendanceHistoryAsync(int studentId, CancellationToken ct = default);
    Task<List<StudentGradeItem>> GradesAsync(int studentId, CancellationToken ct = default);
    Task<List<string>> LevelsAsync(CancellationToken ct = default);
}

public interface IParentService
{
    Task<List<Parent>> ListAsync(CancellationToken ct = default);
    Task<Parent?> GetAsync(int id, CancellationToken ct = default);
    Task<Parent> SaveAsync(Parent parent, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public interface ITeacherService
{
    Task<List<TeacherListItem>> ListAsync(DateTime period, CancellationToken ct = default);
    Task<TeacherDetail?> GetDetailAsync(int id, DateTime period, CancellationToken ct = default);
    Task<Teacher?> GetAsync(int id, CancellationToken ct = default);
    Task<Teacher> SaveAsync(Teacher teacher, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public interface IGroupService
{
    Task<List<Group>> ListAsync(CancellationToken ct = default);
    Task<Group?> GetAsync(int id, CancellationToken ct = default);
    /// <summary>Everything the group page shows: the group (teacher, room, slots, students), revenue and attendance of the month.</summary>
    Task<GroupDetail?> GetDetailAsync(int id, DateTime period, CancellationToken ct = default);
    /// <summary>Saves the group and replaces its timetable slots. Rejects room/teacher overlaps.</summary>
    Task<Group> SaveAsync(Group group, IEnumerable<ScheduleSlot> slots, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<List<string>> FindConflictsAsync(Group group, IEnumerable<ScheduleSlot> slots, CancellationToken ct = default);
}

public interface IScheduleService
{
    /// <summary>All timetable slots of active groups with group, course, subject, teacher and room loaded.</summary>
    Task<List<ScheduleSlot>> WeekAsync(CancellationToken ct = default);
    Task<List<RoomAvailability>> RoomAvailabilityAsync(DateTime at, CancellationToken ct = default);
}

public interface ISessionService
{
    Task<List<Session>> ListAsync(DateTime from, DateTime to, CancellationToken ct = default);
    /// <summary>Creates sessions from the timetable for the date range; existing ones are skipped. Returns the number created.</summary>
    Task<int> GenerateAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<Session> SaveAsync(Session session, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task SetStatusAsync(int id, SessionStatus status, CancellationToken ct = default);
}

public interface IAttendanceService
{
    Task<AttendanceSheet> GetSheetAsync(int sessionId, CancellationToken ct = default);
    Task SaveAsync(int sessionId, IDictionary<int, AttendanceStatus> marks, CancellationToken ct = default);
    Task<AttendanceCounts> CountsForDayAsync(DateTime day, CancellationToken ct = default);
}

public interface IExamService
{
    Task<List<Exam>> ListAsync(CancellationToken ct = default);
    Task<Exam> SaveAsync(Exam exam, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<GradeSheet> GetGradeSheetAsync(int examId, CancellationToken ct = default);
    Task SaveGradesAsync(int examId, IDictionary<int, decimal?> scores, IDictionary<int, string?>? comments = null, CancellationToken ct = default);
}

public interface IPaymentService
{
    /// <summary>Payment situation of every active student today (packs billed, paid, left to pay).</summary>
    Task<List<PaymentRow>> OverviewAsync(CancellationToken ct = default);
    /// <param name="groupId">Group paid for: required for session payments (each group is paid on its own).</param>
    Task<StudentPayment> RecordAsync(int studentId, int? groupId, decimal amount, PaymentMethod method, PaymentKind kind, string? note, CancellationToken ct = default);
    Task<List<StudentPayment>> ReceiptsAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<StudentPayment?> GetReceiptAsync(int paymentId, CancellationToken ct = default);
    /// <summary>Money in and out on <paramref name="day"/> (receipts, cancellations, expenses, teacher payments).</summary>
    Task<CashJournal> CashJournalAsync(DateTime day, CancellationToken ct = default);
    /// <summary>Cancels a receipt (it stays numbered and listed, marked "Annulé", and no longer counts).</summary>
    Task CancelAsync(int paymentId, string reason, CancellationToken ct = default);
}

public interface ITeacherPaymentService
{
    Task<List<TeacherPayRow>> MonthOverviewAsync(DateTime period, CancellationToken ct = default);
    Task<TeacherPayment> RecordAsync(int teacherId, decimal amount, PaymentMethod method, DateTime period, string? note, CancellationToken ct = default);
    Task<List<TeacherPayment>> HistoryAsync(int? teacherId = null, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

/// <summary>Activity journal: what was done in the application (payments, cancellations, students, groups, settings…).</summary>
public interface IAuditService
{
    Task AddAsync(AuditCategory category, string action, string? details = null, CancellationToken ct = default);
    /// <summary>Latest entries first.</summary>
    Task<List<AuditEntry>> ListAsync(AuditCategory? category = null, DateTime? from = null, int take = 500, CancellationToken ct = default);
}

public interface IDashboardService
{
    Task<DashboardData> GetAsync(DateTime now, CancellationToken ct = default);
}

public interface IReportService
{
    Task<FinanceReport> FinanceAsync(DateTime period, CancellationToken ct = default);
    Task<List<GroupAttendanceReport>> AttendanceAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<List<GroupGradeReport>> GradesAsync(CancellationToken ct = default);
}

public interface IExportService
{
    /// <summary>Exports all data to an .xlsx workbook (one sheet per table).</summary>
    Task ExportAllAsync(string path, CancellationToken ct = default);
    Task ExportTableAsync(string path, string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows, CancellationToken ct = default);
}

public interface IDocumentService
{
    Task<List<Document>> ListAsync(DocumentOwnerType? ownerType = null, int? ownerId = null, CancellationToken ct = default);
    Task<Document> AddAsync(string sourcePath, string title, string? category, DocumentOwnerType ownerType, int? ownerId, CancellationToken ct = default);
    Task<Document> SaveAsync(Document document, CancellationToken ct = default);
    string GetFullPath(Document document);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

/// <summary>Local storage for images (logo, photos) and documents, inside the application data folder.</summary>
public interface IFileStorage
{
    string RootFolder { get; }
    /// <summary>Copies the file into storage and returns the stored file name.</summary>
    string Import(string sourcePath, string area);
    string GetPath(string storedFile, string area);
    void Delete(string storedFile, string area);
}

public interface IBackupService
{
    /// <summary>Creates an encrypted backup file and returns its path.</summary>
    Task<string> BackupAsync(string? folder = null, CancellationToken ct = default);
    /// <summary>Restores a backup. The password is the owner's password at the time the backup was made.</summary>
    Task RestoreAsync(string backupFile, string password, CancellationToken ct = default);
    /// <summary>Runs the automatic daily backup if it is due. Returns true when a backup was made.</summary>
    Task<bool> RunScheduledAsync(DateTime now, CancellationToken ct = default);
    string DefaultFolder { get; }
}

/// <summary>Loads realistic sample data into an empty database (for trying the application).</summary>
public interface IDemoDataService
{
    Task<bool> IsDatabaseEmptyAsync(CancellationToken ct = default);
    Task SeedAsync(CancellationToken ct = default);
}

public static class StorageAreas
{
    public const string Images = "images";
    public const string Documents = "documents";
}

/// <summary>Facts about the local installation shown in Settings (storage location, encryption).</summary>
public interface ISystemInfo
{
    bool DatabaseEncrypted { get; }
    /// <summary>How the database key is protected, e.g. "Windows (DPAPI)".</summary>
    string KeyProtection { get; }
    string DataFolder { get; }
    string DatabasePath { get; }
}
