using CentreSoutien.Domain.Enums;

namespace CentreSoutien.Application.Abstractions;

public enum ImportRowStatus
{
    Ok = 0,
    /// <summary>Imported, but something will be left out (unknown group, full group, unknown discount…) or it looks like a duplicate.</summary>
    Warning = 1,
    /// <summary>Not imported (missing name, first name or level, unreadable date).</summary>
    Error = 2,
}

/// <summary>A student row as read from the spreadsheet (values trimmed, not yet checked against the database).</summary>
public sealed record ImportRowData(
    int RowNumber,
    string LastName,
    string FirstName,
    string Level,
    DateTime? BirthDate,
    string? BirthDateText,
    Gender Gender,
    string? GenderText,
    string? School,
    string? Phone,
    string? Address,
    string? ParentName,
    string? ParentPhone,
    string? ParentRelation,
    IReadOnlyList<string> Groups,
    string? Discount,
    string? Notes)
{
    public string FullName => $"{FirstName} {LastName}".Trim();
}

/// <summary>A row with the result of the checks: status, messages and what it matched in the database.</summary>
public sealed record ImportRow(
    ImportRowData Data,
    ImportRowStatus Status,
    IReadOnlyList<string> Messages,
    bool IsDuplicate,
    // "Nouveau parent", "Parent existant : …" or null when the row has no parent.
    string? ParentAction,
    // Full names of the groups the student will be enrolled in.
    IReadOnlyList<string> MatchedGroups,
    string? MatchedDiscount);

public sealed record ImportPreview(string Path, IReadOnlyList<ImportRow> Rows)
{
    public int OkCount => Rows.Count(r => r.Status == ImportRowStatus.Ok);
    public int WarningCount => Rows.Count(r => r.Status == ImportRowStatus.Warning);
    public int ErrorCount => Rows.Count(r => r.Status == ImportRowStatus.Error);
    public int DuplicateCount => Rows.Count(r => r.IsDuplicate && r.Status != ImportRowStatus.Error);

    /// <summary>Rows that will create a student with the given option.</summary>
    public int ImportableCount(bool skipDuplicates) =>
        Rows.Count(r => r.Status != ImportRowStatus.Error && !(skipDuplicates && r.IsDuplicate));
}

public sealed record ImportOptions(bool SkipDuplicates = true);

public sealed record ImportResult(int StudentsCreated, int ParentsCreated, int Enrollments, int Skipped, IReadOnlyList<string> Messages);

/// <summary>Imports students (with parents, group enrollments and discounts) from an Excel workbook.</summary>
public interface IImportService
{
    /// <summary>Writes an .xlsx template: the columns, an example row and a help sheet listing levels, groups and discounts.</summary>
    Task CreateTemplateAsync(string path, CancellationToken ct = default);

    /// <summary>Reads the first sheet and checks every row without changing anything.</summary>
    Task<ImportPreview> PreviewAsync(string path, CancellationToken ct = default);

    /// <summary>Reads, checks and imports the workbook in one transaction.</summary>
    Task<ImportResult> ImportAsync(string path, ImportOptions options, CancellationToken ct = default);

    /// <summary>Imports previewed rows (checked again against the database, in one transaction).</summary>
    Task<ImportResult> ImportAsync(ImportPreview preview, ImportOptions options, CancellationToken ct = default);
}
