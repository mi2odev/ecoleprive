using System.Globalization;
using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;

namespace CentreSoutien.Presentation.Printing;

/// <summary>Turns report card / certificate data into printable pages (formal French wording).</summary>
public static class AcademicDocumentPages
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static PrintPage ReportCard(ReportCardData d, CenterSettings settings, string? appreciation, DateTime issuedOn)
    {
        var s = d.Student;
        var scale = $"/{d.Scale.ToString("0.##", Fr)}";
        string Avg(decimal? v) => v is null ? "—" : Number(v.Value, d.Decimals) + scale;

        var blocks = new List<PrintBlock>
        {
            new PrintFields(
            [
                ("Élève", s.FullName),
                ("Matricule", s.Matricule),
                ("Niveau", string.IsNullOrWhiteSpace(s.Level) ? "—" : s.Level),
                ("Établissement", string.IsNullOrWhiteSpace(s.School) ? "—" : s.School!),
                ("Parent / tuteur", s.ParentName is null ? "—" : string.IsNullOrWhiteSpace(s.ParentPhone) ? s.ParentName : $"{s.ParentName} · {s.ParentPhone}"),
            ]),
            new PrintHeading("Résultats par cours"),
        };

        if (d.Courses.Count == 0)
            blocks.Add(new PrintParagraph("Aucun cours suivi pendant cette période.", Muted: true));
        else
        {
            blocks.Add(new PrintTableBlock(new PrintTable("Résultats par cours",
                ["Cours", "Enseignant", "Moyenne", "Rang", "Moy. du groupe", "Mention"],
                d.Courses.Select(c => (IReadOnlyList<string>)
                [
                    $"{c.Course} ({c.Group})", c.Teacher ?? "—", Avg(c.Average),
                    c.Rank is { } r ? $"{Rank(r)} / {c.RankedCount}" : "—",
                    Avg(c.GroupAverage), Mentions.For(c.Average, d.Scale, d.PassingGrade),
                ]).ToList(), [2, 3, 4])));

            blocks.Add(new PrintHeading("Détail des évaluations"));
            var rows = d.Courses.SelectMany(c => c.Evaluations.Select(e => (IReadOnlyList<string>)
            [
                c.Course, $"{e.Title} ({Labels.Of(e.Type).ToLowerInvariant()})", e.Date.ToString("dd/MM/yyyy"),
                e.Score is { } sc ? $"{sc.ToString("0.##", Fr)} / {e.MaxScore.ToString("0.##", Fr)}" : "Non noté",
                e.Coefficient.ToString("0.##", Fr),
            ])).ToList();
            if (rows.Count == 0) blocks.Add(new PrintParagraph("Aucune évaluation pendant cette période.", Muted: true));
            else blocks.Add(new PrintTableBlock(new PrintTable("Détail des évaluations", ["Cours", "Évaluation", "Date", "Note", "Coef."], rows, [3, 4])));
        }

        blocks.Add(new PrintHeading("Bilan"));
        var a = d.Attendance;
        blocks.Add(new PrintFields(
        [
            ("Moyenne générale", Avg(d.GeneralAverage)),
            ("Mention", Mentions.For(d.GeneralAverage, d.Scale, d.PassingGrade)),
            ("Assiduité", a.Total == 0 ? "Aucune séance relevée" : $"{a.Attended} séance(s) suivie(s) sur {a.Total} · taux de présence {Percent(a.Rate)}"),
            ("Détail des présences", $"{a.Present} présent(s) · {a.Late} retard(s) · {a.Absent} absence(s)" + (a.Excused > 0 ? $" · {a.Excused} excusée(s)" : "")),
        ]));

        blocks.Add(new PrintHeading("Appréciation"));
        blocks.Add(new PrintParagraph(string.IsNullOrWhiteSpace(appreciation) ? "………………………………………………………………………………………………" : appreciation.Trim()));
        blocks.Add(new PrintParagraph(PlaceAndDate(settings, issuedOn), AlignRight: true));
        blocks.Add(new PrintSignature("La direction · Cachet et signature"));

        return new PrintPage("Bulletin de notes", $"{d.Period.Label} · Année scolaire {d.AcademicYear}", blocks);
    }

    public static PrintPage Certificate(CertificateData d, CenterSettings settings)
    {
        var s = d.Student;
        var f = s.Gender == Gender.Female;
        var u = s.Gender == Gender.Unspecified;
        string G(string male, string female, string both) => u ? both : f ? female : male;

        var birth = s.BirthDate is { } b ? $", {G("né", "née", "né(e)")} le {LongDate(b)}" : "";
        var intro = $"Nous soussignés, direction du centre de soutien scolaire « {settings.CenterName} », certifions que " +
                    $"l'élève {s.FullName}{birth}, matricule {s.Matricule}" +
                    (string.IsNullOrWhiteSpace(s.Level) ? "" : $", niveau {s.Level}") +
                    $", est {G("inscrit", "inscrite", "inscrit(e)")} dans notre centre depuis le {LongDate(s.EnrolledOn)} " +
                    $"pour l'année scolaire {d.AcademicYear}" +
                    (d.Courses.Count == 0 ? "." : ", et suit les cours suivants :");

        var blocks = new List<PrintBlock> { new PrintSpacer(12), new PrintParagraph(intro, Size: 13) };
        if (d.Courses.Count > 0)
            blocks.Add(new PrintTableBlock(new PrintTable("Cours suivis", ["Cours", "Groupe", "Enseignant", "Depuis le"],
                d.Courses.Select(c => (IReadOnlyList<string>)[c.Course, c.Group, c.Teacher ?? "—", c.Since.ToString("dd/MM/yyyy")]).ToList())));
        blocks.Add(new PrintSpacer(8));
        blocks.Add(new PrintParagraph($"Le présent certificat est délivré à {G("l'intéressé", "l'intéressée", "l'intéressé(e)")} pour servir et valoir ce que de droit.", Size: 13));
        blocks.Add(new PrintSpacer(12));
        blocks.Add(new PrintParagraph(PlaceAndDate(settings, d.IssuedOn), AlignRight: true));
        blocks.Add(new PrintSignature("La direction · Cachet et signature"));
        return new PrintPage("Certificat de scolarité", $"Année scolaire {d.AcademicYear}", blocks);
    }

    public static PrintPage AttendanceCertificate(AttendanceCertificateData d, CenterSettings settings)
    {
        var s = d.Student;
        var f = s.Gender == Gender.Female;
        var u = s.Gender == Gender.Unspecified;
        string G(string male, string female, string both) => u ? both : f ? female : male;
        var t = d.Total;
        var period = d.Period.IsAcademicYear
            ? $"au cours de l'année scolaire {d.AcademicYear} (du {LongDate(d.Period.From)} au {LongDate(Min(d.Period.To, d.IssuedOn))})"
            : $"durant le mois de {d.Period.Label.ToLowerInvariant()}";

        var text = $"Nous soussignés, direction du centre de soutien scolaire « {settings.CenterName} », attestons que " +
                   $"l'élève {s.FullName}" + (s.BirthDate is { } b ? $", {G("né", "née", "né(e)")} le {LongDate(b)}" : "") +
                   $", matricule {s.Matricule}, {G("inscrit", "inscrite", "inscrit(e)")} dans notre centre, " +
                   (t.Total == 0
                       ? $"n'a aucune séance relevée {period}."
                       : $"a assisté à {t.Attended} séance(s) sur {t.Total} {period}, soit un taux de présence de {Percent(t.Rate)}.");

        var blocks = new List<PrintBlock> { new PrintSpacer(12), new PrintParagraph(text, Size: 13) };
        if (d.Courses.Count > 0)
        {
            var rows = d.Courses.Select(c => (IReadOnlyList<string>)
                [c.Course, c.Attendance.Present.ToString(), c.Attendance.Late.ToString(), c.Attendance.Absent.ToString(), c.Attendance.Excused.ToString(), $"{c.Attendance.Attended} / {c.Attendance.Total}", Percent(c.Attendance.Rate)]).ToList();
            rows.Add(["Total", t.Present.ToString(), t.Late.ToString(), t.Absent.ToString(), t.Excused.ToString(), $"{t.Attended} / {t.Total}", Percent(t.Rate)]);
            blocks.Add(new PrintTableBlock(new PrintTable("Présences", ["Cours", "Présent", "Retard", "Absent", "Excusé", "Séances suivies", "Taux"], rows, [1, 2, 3, 4, 5, 6])));
        }
        blocks.Add(new PrintSpacer(8));
        blocks.Add(new PrintParagraph($"La présente attestation est délivrée à la demande de {G("l'intéressé", "l'intéressée", "l'intéressé(e)")} pour servir et valoir ce que de droit.", Size: 13));
        blocks.Add(new PrintSpacer(12));
        blocks.Add(new PrintParagraph(PlaceAndDate(settings, d.IssuedOn), AlignRight: true));
        blocks.Add(new PrintSignature("La direction · Cachet et signature"));
        return new PrintPage("Attestation de présence", $"{d.Period.Label} · Année scolaire {d.AcademicYear}", blocks);
    }

    /// <summary>"Fait à Alger, le 26 septembre 2026" (city taken from the center address), or "Fait le …" without address.</summary>
    public static string PlaceAndDate(CenterSettings settings, DateTime date)
    {
        var city = City(settings.Address);
        return city is null ? $"Fait le {LongDate(date)}" : $"Fait à {city}, le {LongDate(date)}";
    }

    /// <summary>Last part of an address ("12 rue Didouche, Bab Ezzouar, 16000 Alger" → "Alger"), without postal code.</summary>
    public static string? City(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var parts = address.Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            var words = parts[i].Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !w.All(char.IsDigit)).ToArray();
            if (words.Length > 0 && !string.Equals(words[0], "Algérie", StringComparison.OrdinalIgnoreCase)) return string.Join(' ', words);
        }
        return null;
    }

    /// <summary>"1er octobre 2026", "26 septembre 2026".</summary>
    public static string LongDate(DateTime d) => $"{(d.Day == 1 ? "1er" : d.Day.ToString())} {Labels.Months[d.Month - 1].ToLowerInvariant()} {d.Year}";

    public static string Number(decimal value, int decimals) =>
        value.ToString(decimals <= 0 ? "0" : "0." + new string('0', decimals), Fr);

    public static string Percent(double? rate) => rate is null ? "—" : $"{rate.Value.ToString("0.#", Fr)} %";

    private static string Rank(int r) => r == 1 ? "1er" : $"{r}e";

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
