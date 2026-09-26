using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Services;

/// <summary>
/// Global search (Ctrl+K). A tutoring center holds at most a few thousand rows, so candidates are loaded as small
/// projections and matched in memory: this gives accent-insensitive matching that SQLite's LIKE cannot do.
/// </summary>
public sealed class SearchService(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : ISearchService
{
    public async Task<List<SearchHit>> SearchAsync(string query, int maxPerCategory = 8, CancellationToken ct = default)
    {
        var m = Matcher.Create(query);
        if (m is null) return [];
        await using var db = await factory.CreateDbContextAsync(ct);
        var today = clock.GetLocalNow().Date;
        var results = new List<SearchHit>();

        void Add(IEnumerable<(SearchHit Hit, bool Active)> hits) => results.AddRange(hits
            .OrderBy(h => h.Hit.Rank).ThenByDescending(h => h.Active).ThenBy(h => h.Hit.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(maxPerCategory).Select(h => h.Hit));

        var students = await db.Students.AsNoTracking()
            .Select(s => new { s.Id, s.FirstName, s.LastName, s.Matricule, s.Level, s.Phone, s.IsActive, ParentPhone = s.Parent!.Phone, ParentPhone2 = s.Parent.Phone2 })
            .ToListAsync(ct);
        Add(students.Select(s =>
        {
            var title = $"{s.FirstName} {s.LastName}".Trim();
            var rank = m.Rank(title, [$"{s.LastName} {s.FirstName}", s.Matricule], [s.Phone, s.ParentPhone, s.ParentPhone2]);
            var subtitle = Join(s.Matricule, s.Level, s.ParentPhone is null ? s.Phone : "Parent " + s.ParentPhone, s.IsActive ? null : "Inactif");
            return (new SearchHit(SearchCategory.Student, s.Id, title, subtitle, null, rank), s.IsActive);
        }).Where(h => h.Item1.Rank >= 0));

        var parents = await db.Parents.AsNoTracking()
            .Select(p => new { p.Id, p.FullName, p.Relation, p.Phone, p.Phone2, Children = p.Children.Count })
            .ToListAsync(ct);
        Add(parents.Select(p =>
        {
            var rank = m.Rank(p.FullName, [], [p.Phone, p.Phone2]);
            var subtitle = Join(p.Relation, p.Phone, p.Children == 0 ? null : p.Children == 1 ? "1 enfant" : $"{p.Children} enfants");
            return (new SearchHit(SearchCategory.Parent, p.Id, p.FullName, subtitle, null, rank), true);
        }).Where(h => h.Item1.Rank >= 0));

        var teachers = await db.Teachers.AsNoTracking()
            .Select(t => new { t.Id, t.FirstName, t.LastName, t.Phone, t.IsActive, Subject = t.Subject!.Name, Short = t.Subject.ShortName })
            .ToListAsync(ct);
        Add(teachers.Select(t =>
        {
            var title = $"{t.FirstName} {t.LastName}".Trim();
            var rank = m.Rank(title, [$"{t.LastName} {t.FirstName}", t.Subject, t.Short], [t.Phone]);
            return (new SearchHit(SearchCategory.Teacher, t.Id, title, Join(t.Subject, t.Phone, t.IsActive ? null : "Inactif"), null, rank), t.IsActive);
        }).Where(h => h.Item1.Rank >= 0));

        // Groups (opened on their group page).
        var groups = await db.Groups.AsNoTracking()
            .Select(g => new
            {
                g.Id, g.Name, g.Capacity, Active = g.IsActive, g.Price, g.SessionsPerPack,
                Subject = g.Subject!.Name, Short = g.Subject.ShortName, g.Level,
                Teacher = g.Teacher == null ? null : g.Teacher.FirstName + " " + g.Teacher.LastName,
                Room = g.Room == null ? null : g.Room.Name,
                Enrolled = g.Enrollments.Count(e => e.StartDate <= today && (e.EndDate == null || e.EndDate >= today)),
            })
            .ToListAsync(ct);
        Add(groups.Select(g =>
        {
            var title = $"{g.Subject} · {g.Level} {g.Name}";
            var rank = m.Rank(title, [$"{g.Subject} {g.Level}", $"{g.Short} {g.Level} {g.Name}", $"{g.Level} {g.Subject}", $"groupe {g.Name}"], []);
            var subtitle = Join(g.Teacher, g.Room, $"{g.Enrolled}/{g.Capacity} élèves", $"{Money.Format(g.Price)} / {g.SessionsPerPack} séances", g.Active ? null : "Inactif");
            return (new SearchHit(SearchCategory.Group, g.Id, title, subtitle, g.Id, rank), g.Active);
        }).Where(h => h.Item1.Rank >= 0));

        var documents = await db.Documents.AsNoTracking()
            .Select(d => new { d.Id, d.Title, d.OriginalName, d.Category, d.OwnerType, d.OwnerId, d.CreatedAt })
            .ToListAsync(ct);
        Add(documents.Select(d =>
        {
            var rank = m.Rank(d.Title, [d.OriginalName, d.Category], []);
            return (new SearchHit(SearchCategory.Document, d.Id, d.Title, Join(d.Category, Labels.Of(d.OwnerType), d.CreatedAt.ToString("dd/MM/yyyy")), d.OwnerId, rank), true);
        }).Where(h => h.Item1.Rank >= 0));

        // Receipt numbers always contain digits: skip the (largest) table for plain name searches.
        if (m.HasDigits)
        {
            var receipts = await db.StudentPayments.AsNoTracking()
                .Select(p => new { p.Id, p.ReceiptNumber, p.StudentId, p.Amount, p.Date, p.Student!.FirstName, p.Student.LastName })
                .ToListAsync(ct);
            Add(receipts.Select(p =>
            {
                var rank = m.Rank(p.ReceiptNumber, [], []);
                var subtitle = Join($"{p.FirstName} {p.LastName}", Money.Format(p.Amount), p.Date.ToString("dd/MM/yyyy"));
                return (new SearchHit(SearchCategory.Receipt, p.Id, "Reçu " + p.ReceiptNumber, subtitle, p.StudentId, rank), true);
            }).Where(h => h.Item1.Rank >= 0));
        }

        return results;
    }

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>Multi-word, accent-insensitive matching with a rank: 0 exact, 1 prefix, 2 contains, −1 no match.</summary>
    private sealed class Matcher
    {
        private readonly string _full;
        private readonly string[] _tokens;
        private readonly string _digits;

        private Matcher(string full, string[] tokens, string digits)
        {
            _full = full;
            _tokens = tokens;
            _digits = digits;
        }

        public bool HasDigits => _full.Any(char.IsDigit);

        public static Matcher? Create(string? query)
        {
            var full = TextKey.Normalize(query);
            var tokens = TextKey.Words(query);
            if (tokens.Length == 0) return null;
            // "0550 12 34" or "0550-12-34-56": a phone number typed with separators.
            var digits = full.All(c => char.IsDigit(c) || c is ' ' or '.' or '-' or '+') ? TextKey.Digits(full) : "";
            return new Matcher(full, tokens, digits.Length >= 3 ? digits : "");
        }

        public int Rank(string title, IEnumerable<string?> keys, IEnumerable<string?> phones)
        {
            var fields = new[] { title }.Concat(keys).Select(TextKey.Normalize).Where(f => f.Length > 0).ToList();
            var phoneDigits = phones.Select(TextKey.Digits).Where(p => p.Length > 0).ToList();
            var haystack = string.Join(" ", fields.Concat(phoneDigits));

            var phoneHit = _digits.Length > 0 && phoneDigits.Any(p => p.Contains(_digits, StringComparison.Ordinal));
            if (!phoneHit && !_tokens.All(t => haystack.Contains(t, StringComparison.Ordinal))) return -1;

            if (fields.Any(f => f == _full) || (_digits.Length > 0 && phoneDigits.Any(p => p == _digits))) return 0;
            if (fields.Any(f => f.StartsWith(_full, StringComparison.Ordinal))
                || (_digits.Length > 0 && phoneDigits.Any(p => p.StartsWith(_digits, StringComparison.Ordinal))))
                return 1;
            var words = fields.SelectMany(TextKey.Words).ToList();
            return _tokens.All(t => words.Any(w => w.StartsWith(t, StringComparison.Ordinal))) ? 1 : 2;
        }
    }
}
