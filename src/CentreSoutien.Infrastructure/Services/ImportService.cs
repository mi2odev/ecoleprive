using System.Globalization;
using System.Text.RegularExpressions;
using Group = CentreSoutien.Domain.Entities.Group;
using ClosedXML.Excel;
using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Services;

/// <summary>
/// Student import from Excel: template, preview (checks only) and import (one transaction).
/// Rows are matched against the database with accent/case-insensitive keys (<see cref="TextKey"/>).
/// </summary>
public sealed partial class ImportService(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : IImportService
{
    private enum Col { LastName, FirstName, Level, BirthDate, Gender, School, Phone, Address, ParentName, ParentPhone, Relation, Groups, Discount, Notes }

    private sealed record Column(Col Key, string Header, bool Required, string Help, string[] Aliases);

    private static readonly Column[] Columns =
    [
        new(Col.LastName, "Nom", true, "Nom de famille de l'élève.", ["nom", "nomdefamille", "nomeleve", "nomdeleleve", "lastname"]),
        new(Col.FirstName, "Prénom", true, "Prénom de l'élève.", ["prenom", "prenomeleve", "prenomdeleleve", "prenoms", "firstname"]),
        new(Col.Level, "Niveau", true, "Niveau scolaire, par ex. 4AM, 1AS, 3AS (voir la liste ci-contre).", ["niveau", "classe", "niveauscolaire", "annee"]),
        new(Col.BirthDate, "Date de naissance", false, "Date au format jj/mm/aaaa (ou date Excel).", ["datedenaissance", "datenaissance", "naissance", "nele", "neele", "nee", "birthdate"]),
        new(Col.Gender, "Sexe (G/F)", false, "G (garçon) ou F (fille).", ["sexegf", "sexe", "genre", "sexemf"]),
        new(Col.School, "Établissement", false, "École, CEM ou lycée fréquenté.", ["etablissement", "etablissementscolaire", "ecole", "lycee", "college", "cem"]),
        new(Col.Phone, "Téléphone élève", false, "Numéro de l'élève (facultatif).", ["telephoneeleve", "teleleve", "telephonedeleleve", "telephone", "tel", "mobile", "portable"]),
        new(Col.Address, "Adresse", false, "Adresse de l'élève.", ["adresse", "domicile"]),
        new(Col.ParentName, "Parent (nom)", false, "Nom du parent. Un parent existant est retrouvé par son téléphone, sinon par son nom ; sinon il est créé.", ["parentnom", "parent", "nomduparent", "nomparent", "tuteur", "nomdututeur"]),
        new(Col.ParentPhone, "Téléphone parent", false, "Numéro du parent (sert à retrouver un parent déjà enregistré, par ex. pour les frères et sœurs).", ["telephoneparent", "telparent", "telephoneduparent", "teldesparents", "telephonedesparents"]),
        new(Col.Relation, "Lien (Père/Mère/Tuteur)", false, "Père, Mère ou Tuteur.", ["lienperemeretuteur", "lien", "relation", "liendeparente", "lienparente", "qualite"]),
        new(Col.Groups, "Groupes", false, "Groupes séparés par « ; », par ex. « Mathématiques 3AS A; Physique 3AS A » (voir la liste ci-contre). Un groupe complet ou introuvable est ignoré.", ["groupes", "groupe", "cours", "matieres"]),
        new(Col.Discount, "Remise", false, "Nom d'une remise existante (voir la liste ci-contre).", ["remise", "reduction", "remises"]),
        new(Col.Notes, "Notes", false, "Remarques libres.", ["notes", "note", "remarques", "remarque", "observations", "observation", "commentaire"]),
    ];

    private static readonly string[] StandardLevels = ["1AP", "2AP", "3AP", "4AP", "5AP", "1AM", "2AM", "3AM", "4AM", "1AS", "2AS", "3AS"];

    // ============================== Template ==============================

    public async Task CreateTemplateAsync(string path, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ctx = await LoadAsync(db, ct);
        using var wb = new XLWorkbook();

        var ws = wb.Worksheets.Add("Élèves");
        for (var c = 0; c < Columns.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = Columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml(Columns[c].Required ? "#D9E1EC" : "#EBEFF4");
        }

        // Example row (to be replaced by real students).
        var example = ctx.Groups.Take(2).ToList();
        var exampleLevel = example.FirstOrDefault()?.Group.Level ?? "3AS";
        var exampleGroups = example.Count > 0
            ? string.Join("; ", example.Where(g => g.Group.Level == exampleLevel).Select(g => g.Label))
            : "Mathématiques 3AS A; Physique 3AS A";
        object?[] values =
        [
            "Benali", "Yacine", exampleLevel, new DateTime(2008, 3, 12), "G", "Lycée Émir Abdelkader", "0550 12 34 56", "Bab Ezzouar, Alger",
            "Karim Benali", "0661 23 45 67", "Père", exampleGroups, null, "Ligne d'exemple : à remplacer",
        ];
        for (var c = 0; c < values.Length; c++)
        {
            var cell = ws.Cell(2, c + 1);
            switch (values[c])
            {
                case DateTime d: cell.Value = d; break;
                case string s: cell.Value = s; break;
            }
            cell.Style.Font.Italic = true;
            cell.Style.Font.FontColor = XLColor.FromHtml("#6A6A66");
        }
        ws.Range(2, 4, 500, 4).Style.DateFormat.Format = "dd/mm/yyyy";
        foreach (var col in new[] { Col.Phone, Col.ParentPhone })
            ws.Range(2, (int)col + 1, 500, (int)col + 1).Style.NumberFormat.Format = "@"; // keeps the leading 0 of phone numbers
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        ws.Column((int)Col.Groups + 1).Width = Math.Max(ws.Column((int)Col.Groups + 1).Width, 36);

        // Help sheet: what each column means and the values the center already uses.
        var help = wb.Worksheets.Add("Aide");
        help.Cell(1, 1).Value = "Importer des élèves — mode d'emploi";
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Cell(1, 1).Style.Font.FontSize = 14;
        Header(help, 3, 1, "Colonne");
        Header(help, 3, 2, "Obligatoire");
        Header(help, 3, 3, "Description");
        for (var i = 0; i < Columns.Length; i++)
        {
            help.Cell(4 + i, 1).Value = Columns[i].Header;
            help.Cell(4 + i, 2).Value = Columns[i].Required ? "Oui" : "Non";
            help.Cell(4 + i, 3).Value = Columns[i].Help;
        }
        var r = 5 + Columns.Length;
        string[] notes =
        [
            "Remplissez la feuille « Élèves » : une ligne par élève, à partir de la ligne 2 (supprimez la ligne d'exemple).",
            "L'ordre des colonnes n'a pas d'importance et les colonnes supplémentaires sont ignorées.",
            "Les matricules sont attribués automatiquement.",
            "Avant l'import, un aperçu indique pour chaque ligne : OK, Avertissement (sera importée en partie) ou Erreur (ne sera pas importée).",
            "Un élève déjà enregistré avec le même nom et la même date de naissance est signalé comme doublon.",
        ];
        foreach (var n in notes) help.Cell(r++, 1).Value = "• " + n;

        var listCol = 5;
        Header(help, 3, listCol, "Niveaux");
        var levels = ctx.Levels.Union(StandardLevels).Distinct().OrderBy(Levels.Order).ToList();
        for (var i = 0; i < levels.Count; i++) help.Cell(4 + i, listCol).Value = levels[i];

        Header(help, 3, listCol + 2, "Groupes (à recopier)");
        Header(help, 3, listCol + 3, "Places libres");
        for (var i = 0; i < ctx.Groups.Count; i++)
        {
            help.Cell(4 + i, listCol + 2).Value = ctx.Groups[i].Label;
            help.Cell(4 + i, listCol + 3).Value = Math.Max(0, ctx.Groups[i].Group.Capacity - ctx.Groups[i].Active);
        }
        if (ctx.Groups.Count == 0) help.Cell(4, listCol + 2).Value = "Aucun groupe enregistré";

        Header(help, 3, listCol + 5, "Remises");
        Header(help, 3, listCol + 6, "Valeur");
        var discounts = ctx.Discounts.Where(d => d.IsActive).ToList();
        for (var i = 0; i < discounts.Count; i++)
        {
            help.Cell(4 + i, listCol + 5).Value = discounts[i].Name;
            help.Cell(4 + i, listCol + 6).Value = discounts[i].ValueLabel;
        }
        if (discounts.Count == 0) help.Cell(4, listCol + 5).Value = "Aucune remise enregistrée";
        help.Columns().AdjustToContents();
        help.Column(3).Width = 70;
        help.Column(3).Style.Alignment.WrapText = true;

        try
        {
            wb.SaveAs(path);
        }
        catch (IOException)
        {
            throw new BusinessException("Impossible d'enregistrer le modèle. Le fichier est peut-être ouvert dans Excel.");
        }
    }

    private static void Header(IXLWorksheet ws, int row, int col, string text)
    {
        var cell = ws.Cell(row, col);
        cell.Value = text;
        cell.Style.Font.Bold = true;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EBEFF4");
    }

    // ============================== Preview / import ==============================

    public async Task<ImportPreview> PreviewAsync(string path, CancellationToken ct = default)
    {
        var rows = Read(path);
        await using var db = await factory.CreateDbContextAsync(ct);
        var ctx = await LoadAsync(db, ct);
        return new ImportPreview(path, Evaluate(ctx, rows, skipDuplicates: true).Select(p => p.Row).ToList());
    }

    public Task<ImportResult> ImportAsync(string path, ImportOptions options, CancellationToken ct = default) =>
        ImportRowsAsync(Read(path), options, ct);

    public Task<ImportResult> ImportAsync(ImportPreview preview, ImportOptions options, CancellationToken ct = default) =>
        ImportRowsAsync(preview.Rows.Select(r => r.Data).ToList(), options, ct);

    private async Task<ImportResult> ImportRowsAsync(IReadOnlyList<ImportRowData> rows, ImportOptions options, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ctx = await LoadAsync(db, ct);
        var plans = Evaluate(ctx, rows, options.SkipDuplicates);

        var next = StudentService.NextMatriculeNumber(ctx.Students.Select(s => s.Matricule));
        int created = 0, parentsCreated = 0, enrollments = 0, skipped = 0;
        var messages = new List<string>();
        var newParents = new HashSet<Parent>();

        foreach (var plan in plans)
        {
            var d = plan.Row.Data;
            if (!plan.Import)
            {
                skipped++;
                messages.Add(plan.Row.Status == ImportRowStatus.Error
                    ? $"Ligne {d.RowNumber} ignorée : {string.Join(" ; ", plan.Row.Messages)}"
                    : $"Ligne {d.RowNumber} ignorée (doublon) : {d.FullName}");
                continue;
            }
            var student = new Student
            {
                Matricule = "E" + next++,
                LastName = d.LastName,
                FirstName = d.FirstName,
                Level = d.Level,
                BirthDate = d.BirthDate,
                Gender = d.Gender,
                School = d.School,
                Phone = d.Phone,
                Address = d.Address,
                Notes = d.Notes,
                EnrolledOn = ctx.Today,
                IsActive = true,
                DiscountId = plan.Discount?.Id,
            };
            if (plan.Parent is { Id: 0 } np)
            {
                student.Parent = np;
                if (newParents.Add(np)) parentsCreated++;
            }
            else student.ParentId = plan.Parent?.Id;
            foreach (var g in plan.Groups)
            {
                student.Enrollments.Add(new Enrollment { GroupId = g.Id, StartDate = ctx.Today });
                enrollments++;
            }
            db.Students.Add(student);
            created++;
            foreach (var m in plan.Row.Messages) messages.Add($"Ligne {d.RowNumber} ({d.FullName}) : {m}");
        }

        if (created > 0)
            db.AuditLog.Add(Audit.Entry(clock, Domain.Enums.AuditCategory.Student,
                $"Import Excel : {created} élève{(created > 1 ? "s" : "")} ajouté{(created > 1 ? "s" : "")}",
                $"{parentsCreated} parent(s) · {enrollments} inscription(s) · {skipped} ligne(s) ignorée(s)"));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new ImportResult(created, parentsCreated, enrollments, skipped, messages);
    }

    // ============================== Reading ==============================

    private static List<ImportRowData> Read(string path)
    {
        if (!File.Exists(path)) throw new BusinessException("Fichier introuvable : " + path);
        XLWorkbook wb;
        try
        {
            // FileShare.ReadWrite: the file can still be read while it is open in Excel.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            wb = new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is not BusinessException and not OutOfMemoryException)
        {
            throw new BusinessException("Impossible de lire ce fichier. Choisissez un classeur Excel (.xlsx).");
        }

        using (wb)
        {
            var ws = wb.Worksheets.First();
            var used = ws.RangeUsed() ?? throw new BusinessException("La première feuille du classeur est vide.");
            var firstRow = used.FirstRow().RowNumber();
            var lastRow = used.LastRow().RowNumber();
            var lastCol = used.LastColumn().ColumnNumber();

            // Header row: the first of the first ten rows that has both "Nom" and "Prénom".
            Dictionary<Col, int>? map = null;
            var headerRow = 0;
            for (var r = firstRow; r <= Math.Min(firstRow + 9, lastRow) && map is null; r++)
            {
                var m = MatchHeaders(ws, r, lastCol);
                if (m.ContainsKey(Col.LastName) && m.ContainsKey(Col.FirstName))
                {
                    map = m;
                    headerRow = r;
                }
            }
            if (map is null)
                throw new BusinessException("Ligne d'en-tête introuvable : la première feuille doit contenir au moins les colonnes « Nom », « Prénom » et « Niveau ».");
            if (!map.ContainsKey(Col.Level))
                throw new BusinessException("La colonne « Niveau » est introuvable dans la première feuille.");

            var rows = new List<ImportRowData>();
            for (var r = headerRow + 1; r <= lastRow; r++)
            {
                IXLCell? Cell(Col c) => map.TryGetValue(c, out var col) ? ws.Cell(r, col) : null;
                string? Get(Col c) => Cell(c) is { } cell ? Text(cell) : null;
                if (map.Values.All(c => Text(ws.Cell(r, c)) is null)) continue;

                var (birth, birthText) = Cell(Col.BirthDate) is { } bc ? ReadDate(bc) : (null, null);
                var genderText = Get(Col.Gender);
                rows.Add(new ImportRowData(
                    r,
                    Get(Col.LastName) ?? "",
                    Get(Col.FirstName) ?? "",
                    NormalizeLevel(Get(Col.Level)),
                    birth,
                    birthText,
                    ParseGender(genderText),
                    genderText,
                    Get(Col.School),
                    Cell(Col.Phone) is { } pc ? Phone(pc) : null,
                    Get(Col.Address),
                    Get(Col.ParentName),
                    Cell(Col.ParentPhone) is { } ppc ? Phone(ppc) : null,
                    NormalizeRelation(Get(Col.Relation)),
                    (Get(Col.Groups) ?? "").Split([';', '\n', '|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    Get(Col.Discount),
                    Get(Col.Notes)));
            }
            return rows;
        }
    }

    private static Dictionary<Col, int> MatchHeaders(IXLWorksheet ws, int row, int lastCol)
    {
        var headers = Enumerable.Range(1, lastCol).Select(c => (Col: c, Key: TextKey.Compact(Text(ws.Cell(row, c))))).Where(h => h.Key.Length > 0).ToList();
        var map = new Dictionary<Col, int>();
        // Exact aliases first, then "starts with" (e.g. "Nom de l'élève (obligatoire)"), longest alias first.
        foreach (var (col, key) in headers)
        {
            var match = Columns.FirstOrDefault(c => !map.ContainsKey(c.Key) && c.Aliases.Contains(key));
            if (match is not null) map[match.Key] = col;
        }
        foreach (var (col, key) in headers)
        {
            if (map.ContainsValue(col)) continue;
            var match = Columns.SelectMany(c => c.Aliases.Select(a => (c.Key, Alias: a)))
                .Where(x => !map.ContainsKey(x.Key) && x.Alias.Length >= 3 && key.StartsWith(x.Alias, StringComparison.Ordinal))
                .OrderByDescending(x => x.Alias.Length)
                .FirstOrDefault();
            if (match.Alias is not null) map[match.Key] = col;
        }
        return map;
    }

    private static string? Text(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        var s = cell.DataType switch
        {
            XLDataType.Number => cell.GetDouble().ToString(CultureInfo.InvariantCulture),
            XLDataType.DateTime => cell.GetDateTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            _ => cell.GetFormattedString(),
        };
        s = s.Trim();
        return s.Length == 0 ? null : s;
    }

    /// <summary>Phone numbers typed as numbers lose their leading 0 in Excel (550123456 → 0550123456).</summary>
    private static string? Phone(IXLCell cell)
    {
        var t = Text(cell);
        if (t is null) return null;
        if (cell.DataType == XLDataType.Number && t.Length == 9 && t[0] is '5' or '6' or '7') t = "0" + t;
        return t;
    }

    private static readonly string[] DateFormats =
        ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd/MM/yy", "d/M/yy", "dd/MM/yyyy HH:mm:ss", "d/M/yyyy H:mm:ss"];

    /// <summary>Returns the date, and the raw text when the cell holds something (for display and error messages).</summary>
    private static (DateTime? Value, string? Text) ReadDate(IXLCell cell)
    {
        if (cell.IsEmpty()) return (null, null);
        DateTime? value = null;
        var text = Text(cell);
        if (text is null) return (null, null);
        if (cell.DataType == XLDataType.DateTime) value = cell.GetDateTime().Date;
        else if (cell.DataType == XLDataType.Number)
        {
            var d = cell.GetDouble();
            if (d is > 1 and < 2958465) value = DateTime.FromOADate(d).Date;
        }
        else if (DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            value = parsed.Date;
        if (value is { } v && (v.Year < 1940 || v > DateTime.Today)) value = null;
        return (value, value is { } ok ? ok.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : text);
    }

    private static string NormalizeLevel(string? text) => WhiteSpace().Replace(text ?? "", "").ToUpperInvariant();

    private static Gender ParseGender(string? text) => TextKey.Compact(text) switch
    {
        "" => Gender.Unspecified,
        "g" or "m" or "h" or "garcon" or "masculin" or "homme" or "male" => Gender.Male,
        "f" or "fille" or "feminin" or "femme" or "female" => Gender.Female,
        _ => Gender.Unspecified,
    };

    private static string? NormalizeRelation(string? text) => TextKey.Compact(text) switch
    {
        "" => null,
        "pere" or "p" or "papa" => "Père",
        "mere" or "maman" => "Mère",
        "tuteur" or "tutrice" or "t" => "Tuteur",
        _ => text,
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    // ============================== Checks ==============================

    private sealed record ExistingStudent(int Id, string Matricule, string FirstName, string LastName, DateTime? BirthDate);

    private sealed class GroupInfo(Group group, int active)
    {
        public Group Group => group;
        public int Active => active;
        /// <summary>Students planned into the group by earlier rows of the file.</summary>
        public int Planned { get; set; }
        public bool IsFull => Active + Planned >= Group.Capacity;
        public string Label => $"{Group.Subject?.Name} {Group.Level} {Group.Name}";
    }

    private sealed class Context
    {
        public required DateTime Today { get; init; }
        public required List<ExistingStudent> Students { get; init; }
        public required List<Parent> Parents { get; init; }
        public required List<GroupInfo> Groups { get; init; }
        public required List<Discount> Discounts { get; init; }
        public required List<string> Levels { get; init; }
        public Dictionary<string, GroupInfo> GroupKeys { get; } = [];
        public Dictionary<string, List<GroupInfo>> CourseKeys { get; } = [];
    }

    private sealed record Plan(ImportRow Row, bool Import, Parent? Parent, List<Group> Groups, Discount? Discount);

    private async Task<Context> LoadAsync(AppDbContext db, CancellationToken ct)
    {
        var today = clock.GetLocalNow().Date;
        var groups = await db.Groups.AsNoTracking()
            .Include(g => g.Subject)
            .Include(g => g.Enrollments)
            .AsSplitQuery()
            .Where(g => g.IsActive && g.IsActive)
            .ToListAsync(ct);
        var ctx = new Context
        {
            Today = today,
            Students = await db.Students.AsNoTracking().Select(s => new ExistingStudent(s.Id, s.Matricule, s.FirstName, s.LastName, s.BirthDate)).ToListAsync(ct),
            Parents = await db.Parents.AsNoTracking().ToListAsync(ct),
            Groups = groups
                .OrderBy(g => g.Subject!.Name).ThenBy(g => Levels.Order(g.Level)).ThenBy(g => g.Name)
                .Select(g => new GroupInfo(g, g.Enrollments.Count(e => e.IsActiveOn(today)))).ToList(),
            Discounts = await db.Discounts.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct),
            Levels = await db.Groups.Select(g => g.Level).Union(db.Students.Select(s => s.Level)).ToListAsync(ct),
        };
        ctx.Levels.RemoveAll(string.IsNullOrWhiteSpace);

        foreach (var g in ctx.Groups)
        {
            var subject = g.Group.Subject;
            var names = new[] { subject?.Name, subject?.ShortName }.Select(TextKey.Compact).Where(n => n.Length > 0).Distinct().ToList();
            var level = TextKey.Compact(g.Group.Level);
            var name = TextKey.Compact(g.Group.Name);
            foreach (var n in names)
            {
                foreach (var key in new[] { n + level + name, n + level + "groupe" + name, n + level + "gr" + name, level + n + name })
                    ctx.GroupKeys.TryAdd(key, g);
                foreach (var key in new[] { n + level, level + n })
                {
                    if (!ctx.CourseKeys.TryGetValue(key, out var list)) ctx.CourseKeys[key] = list = [];
                    list.Add(g);
                }
            }
        }
        return ctx;
    }

    private static string StudentKey(string first, string last, DateTime? birth) =>
        $"{TextKey.Compact(last)}|{TextKey.Compact(first)}|{birth:yyyyMMdd}";

    private static List<Plan> Evaluate(Context ctx, IReadOnlyList<ImportRowData> rows, bool skipDuplicates)
    {
        var existing = ctx.Students.GroupBy(s => StudentKey(s.FirstName, s.LastName, s.BirthDate)).ToDictionary(g => g.Key, g => g.First());
        var inFile = new Dictionary<string, int>();
        var plans = new List<Plan>();

        foreach (var d in rows)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            if (d.LastName.Length == 0) errors.Add("Nom manquant");
            if (d.FirstName.Length == 0) errors.Add("Prénom manquant");
            if (d.Level.Length == 0) errors.Add("Niveau manquant");
            if (d.BirthDate is null && d.BirthDateText is not null) errors.Add($"Date de naissance illisible « {d.BirthDateText} » (format jj/mm/aaaa)");

            var duplicate = false;
            if (d.LastName.Length > 0 && d.FirstName.Length > 0)
            {
                var key = StudentKey(d.FirstName, d.LastName, d.BirthDate);
                if (existing.TryGetValue(key, out var ex))
                {
                    duplicate = true;
                    warnings.Add($"Doublon probable : {ex.Matricule} {ex.FirstName} {ex.LastName} existe déjà (même nom et date de naissance)");
                }
                else if (inFile.TryGetValue(key, out var other))
                {
                    duplicate = true;
                    warnings.Add($"Doublon de la ligne {other}");
                }
                else if (errors.Count == 0) inFile[key] = d.RowNumber;
            }

            if (errors.Count > 0)
            {
                plans.Add(new Plan(new ImportRow(d, ImportRowStatus.Error, errors, duplicate, null, [], null), false, null, [], null));
                continue;
            }
            var import = !(duplicate && skipDuplicates);

            if (!StandardLevels.Contains(d.Level) && !ctx.Levels.Contains(d.Level, StringComparer.OrdinalIgnoreCase))
                warnings.Add($"Niveau « {d.Level} » inhabituel (ni dans la liste des niveaux, ni utilisé par un cours)");
            if (d.GenderText is not null && d.Gender == Gender.Unspecified)
                warnings.Add($"Sexe « {d.GenderText} » non reconnu (G ou F) : laissé vide");

            // Groups: exact group, or a course with a single group (or the first one with room).
            var groups = new List<Group>();
            foreach (var text in d.Groups)
            {
                var key = TextKey.Compact(text);
                GroupInfo? g = null;
                if (ctx.GroupKeys.TryGetValue(key, out var exact)) g = exact;
                else if (ctx.CourseKeys.TryGetValue(key, out var candidates))
                {
                    g = candidates.FirstOrDefault(c => !c.IsFull) ?? candidates[0];
                    if (candidates.Count > 1 && !g.IsFull) warnings.Add($"Groupe non précisé pour « {text} » : groupe {g.Group.Name} choisi");
                }
                if (g is null)
                {
                    warnings.Add($"Groupe introuvable « {text} »");
                    continue;
                }
                if (groups.Contains(g.Group)) continue;
                if (g.IsFull)
                {
                    warnings.Add($"Groupe complet : {g.Group.FullName} ({g.Group.Capacity} places), élève non inscrit");
                    continue;
                }
                if (!string.Equals(g.Group.Level, d.Level, StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"Le groupe {g.Group.FullName} n'est pas du niveau {d.Level}");
                groups.Add(g.Group);
                if (import) g.Planned++;
            }

            Discount? discount = null;
            if (d.Discount is not null)
            {
                var key = TextKey.Compact(d.Discount);
                discount = ctx.Discounts.FirstOrDefault(x => x.IsActive && TextKey.Compact(x.Name) == key)
                    ?? ctx.Discounts.FirstOrDefault(x => x.IsActive && TextKey.Compact(x.ToString()) == key);
                if (discount is null) warnings.Add($"Remise introuvable « {d.Discount} »");
            }

            // Parent: by phone, else by name, else a new one (shared with later rows: siblings).
            Parent? parent = null;
            string? parentAction = null;
            if (d.ParentName is not null || d.ParentPhone is not null)
            {
                var phone = TextKey.Digits(d.ParentPhone);
                if (phone.Length >= 6)
                    parent = ctx.Parents.FirstOrDefault(p => TextKey.Digits(p.Phone) == phone || TextKey.Digits(p.Phone2) == phone);
                if (parent is null && d.ParentName is not null)
                {
                    var name = TextKey.Compact(d.ParentName);
                    parent = ctx.Parents.FirstOrDefault(p => TextKey.Compact(p.FullName) == name);
                }
                if (parent is not null)
                    parentAction = parent.Id == 0 ? $"Nouveau parent (ligne précédente) : {parent.FullName}" : $"Parent existant : {parent.FullName}";
                else
                {
                    var fullName = d.ParentName;
                    if (fullName is null)
                    {
                        fullName = "Parent de " + d.FullName;
                        warnings.Add($"Parent sans nom : créé sous le nom « {fullName} »");
                    }
                    parent = new Parent { FullName = fullName, Phone = d.ParentPhone, Relation = d.ParentRelation, Address = d.Address };
                    parentAction = "Nouveau parent : " + fullName;
                    if (import) ctx.Parents.Add(parent);
                }
            }

            var status = warnings.Count > 0 ? ImportRowStatus.Warning : ImportRowStatus.Ok;
            plans.Add(new Plan(new ImportRow(d, status, warnings, duplicate, parentAction, groups.Select(g => g.FullName).ToList(), discount?.Name),
                import, parent, groups, discount));
        }
        return plans;
    }
}
