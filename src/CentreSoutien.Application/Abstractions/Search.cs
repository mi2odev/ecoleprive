using System.Globalization;
using System.Text;

namespace CentreSoutien.Application.Abstractions;

public enum SearchCategory
{
    Student = 0,
    Parent = 1,
    Teacher = 2,
    Group = 3,
    Document = 4,
    Receipt = 5,
}

/// <summary>
/// One global-search result. <see cref="Id"/> is the entity found; <see cref="RelatedId"/> is what the UI needs to open it
/// when that is another entity (group → course id, receipt → student id, document → owner id).
/// </summary>
public sealed record SearchHit(SearchCategory Category, int Id, string Title, string? Subtitle, int? RelatedId = null, int Rank = 0);

public interface ISearchService
{
    /// <summary>
    /// Accent- and case-insensitive search across students, parents, teachers, courses/groups, documents and receipts.
    /// Every word of the query must match; results are ordered by category, then exact / prefix matches first.
    /// </summary>
    Task<List<SearchHit>> SearchAsync(string query, int maxPerCategory = 8, CancellationToken ct = default);
}

/// <summary>Text normalisation shared by search and import ("Bénali" ≈ "benali").</summary>
public static class TextKey
{
    /// <summary>Lower case, without diacritics, trimmed, inner whitespace collapsed.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var decomposed = text.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var space = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c))
            {
                if (!space) sb.Append(' ');
                space = true;
                continue;
            }
            space = false;
            sb.Append(c switch { 'œ' or 'Œ' => "oe", 'æ' or 'Æ' => "ae", _ => char.ToLowerInvariant(c).ToString() });
        }
        return sb.ToString().Trim();
    }

    /// <summary>Normalised and reduced to letters and digits only (header matching, loose name comparison).</summary>
    public static string Compact(string? text) => new(Normalize(text).Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Words of the normalised text (punctuation treated as a separator).</summary>
    public static string[] Words(string? text) =>
        new string(Normalize(text).Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Digits only (phone numbers typed with spaces, dots or dashes).</summary>
    public static string Digits(string? text) => text is null ? "" : new(text.Where(char.IsDigit).ToArray());
}
