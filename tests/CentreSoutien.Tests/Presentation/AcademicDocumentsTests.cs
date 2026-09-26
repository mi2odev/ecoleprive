using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.Printing;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CentreSoutien.Presentation.ViewModels.Pages;

namespace CentreSoutien.Tests.Presentation;

public class AcademicDocumentsTests
{
    private static string Text(PrintPage page) => string.Join("\n",
        new[] { page.Title, page.Subtitle ?? "" }.Concat(page.Blocks.Select(b => b switch
        {
            PrintHeading h => h.Text,
            PrintParagraph p => p.Text,
            PrintFields f => string.Join("\n", f.Rows.Select(r => $"{r.Label}: {r.Value}")),
            PrintTableBlock t => string.Join("\n", t.Table.Headers.Concat(t.Table.Rows.Select(r => string.Join(" | ", r)))),
            PrintSignature s => s.Label,
            _ => "",
        })));

    private static async Task<T> DialogAsync<T>(TestHost host) where T : DialogViewModel
    {
        for (var i = 0; i < 200; i++)
        {
            if (host.Get<DialogHost>().Current is T d) return d;
            await Task.Delay(10);
        }
        throw new InvalidOperationException($"Dialog {typeof(T).Name} not shown (current: {host.Get<DialogHost>().Current?.GetType().Name}).");
    }

    private static async Task<Group> GroupAsync(TestHost host, string fullName)
    {
        var id = (await host.Get<IGroupService>().ListAsync()).First(g => g.FullName == fullName).Id;
        return (await host.Get<IGroupService>().GetAsync(id))!;
    }

    private static async Task<StudentDetailViewModel> OpenStudentAsync(TestHost host, int studentId)
    {
        await host.Get<Navigator>().NavigateAsync<StudentDetailViewModel>(studentId);
        var page = host.Page<StudentDetailViewModel>();
        Assert.False(page.HasError, page.Error);
        return page;
    }

    private static async Task<(string Job, IReadOnlyList<PrintPage> Pages)> PrintFromStudentAsync(
        TestHost host, StudentDetailViewModel page, Action<PrintDocumentsDialogViewModel> choose)
    {
        var fake = host.Get<FakePlatform>();
        var before = fake.PrintedPages.Count;
        var run = page.PrintDocumentsCommand.ExecuteAsync(null);
        var dialog = await DialogAsync<PrintDocumentsDialogViewModel>(host);
        choose(dialog);
        await dialog.ConfirmCommand.ExecuteAsync(null);
        await run;
        Assert.False(dialog.HasError, dialog.Error);
        Assert.Equal(before + 1, fake.PrintedPages.Count);
        return fake.PrintedPages[^1];
    }

    [Fact]
    public async Task Student_report_card_shows_averages_mentions_rank_and_attendance()
    {
        await using var host = await UiHost.CreateAsync();
        var cfg = await host.Get<ISettingsService>().GetAsync();
        cfg.Address = "12 rue Didouche Mourad, 16000 Alger";
        await host.Get<ISettingsService>().SaveAsync(cfg);

        var group = await GroupAsync(host, "Mathématiques · 3AS A");
        var student = group.Enrollments.First().Student!;
        var page = await OpenStudentAsync(host, student.Id);

        var (job, pages) = await PrintFromStudentAsync(host, page, d =>
        {
            Assert.False(d.IsGroupMode);
            Assert.True(d.ShowTypes);
            Assert.Equal(AcademicDocumentType.ReportCard, d.SelectedType!.Value);
            Assert.True(d.ShowPeriod);
            Assert.True(d.ShowAppreciation);
            Assert.Equal("Septembre 2026", d.SelectedPeriod!.Label);
            Assert.Equal("Année scolaire 2026–2027", d.Periods[0].Label);
            d.Appreciation = "Élève sérieux, bons progrès.";
        });

        Assert.Contains("Bulletin", job);
        var p = Assert.Single(pages);
        Assert.Equal("Bulletin de notes", p.Title);
        Assert.Contains("Septembre 2026", p.Subtitle);
        var text = Text(p);
        Assert.Contains(student.FullName, text);
        Assert.Contains(student.Matricule, text);
        Assert.Contains("Mathématiques · 3AS (A)", text);
        Assert.Contains(group.Teacher!.FullName, text);
        Assert.Contains("Test 1", text);
        Assert.Contains("Devoir 1", text);
        Assert.Contains("Moyenne générale", text);
        Assert.Matches(@"Mention: (Très bien|Bien|Assez bien|Passable|Insuffisant)", text);
        Assert.Contains("taux de présence", text);
        Assert.Contains("Élève sérieux, bons progrès.", text);
        Assert.Contains("Fait à Alger, le 26 septembre 2026", text);
        Assert.Contains(p.Blocks, b => b is PrintSignature);

        // Averages match the ones shown on the student's grades tab; attendance matches the history.
        var card = await host.Get<IAcademicDocumentsService>().ReportCardAsync(student.Id, DocumentPeriod.Month(new DateTime(2026, 9, 26)));
        foreach (var row in page.Grades)
        {
            var course = card.Courses.Single(c => $"{c.Course} {c.Group}" == row.Group);
            Assert.Equal(row.Average, course.Average!.Value.ToString("0.00"));
            Assert.NotNull(course.Rank);
            Assert.InRange(course.Rank!.Value, 1, course.RankedCount);
            Assert.Contains(AcademicDocumentPages.Number(course.Average.Value, 2) + "/20", text);
        }
        Assert.NotNull(card.GeneralAverage);
        Assert.Equal(page.Attendance.Count, card.Attendance.Total);
    }

    [Fact]
    public async Task Certificate_names_the_student_and_the_courses()
    {
        await using var host = await UiHost.CreateAsync();
        var group = await GroupAsync(host, "Physique · 3AS A");
        var student = group.Enrollments.First().Student!;
        var page = await OpenStudentAsync(host, student.Id);

        var (_, pages) = await PrintFromStudentAsync(host, page, d =>
        {
            d.SelectedType = d.Types.Single(t => t.Value == AcademicDocumentType.Certificate);
            Assert.False(d.ShowPeriod);
            Assert.False(d.ShowAppreciation);
        });

        var p = Assert.Single(pages);
        Assert.Equal("Certificat de scolarité", p.Title);
        var text = Text(p);
        Assert.Contains("Nous soussignés", text);
        Assert.Contains("certifions que l'élève " + student.FullName, text);
        Assert.Contains("2026–2027", text);
        Assert.Contains(student.Gender == Gender.Female ? "inscrite" : "inscrit", text);
        foreach (var e in page.Enrollments)
            Assert.Contains(e.Name[..e.Name.LastIndexOf(' ')], text); // "Physique · 3AS"
        Assert.Contains("Physique · 3AS", text);
        Assert.Contains("Fait le 26 septembre 2026", text); // no center address configured
    }

    [Fact]
    public async Task Attendance_certificate_counts_sessions_of_the_academic_year()
    {
        await using var host = await UiHost.CreateAsync();
        var group = await GroupAsync(host, "Mathématiques · 3AS A");
        var student = group.Enrollments.First().Student!;
        var page = await OpenStudentAsync(host, student.Id);

        var (_, pages) = await PrintFromStudentAsync(host, page, d =>
        {
            d.SelectedType = d.Types.Single(t => t.Value == AcademicDocumentType.Attendance);
            Assert.True(d.ShowPeriod);
            Assert.False(d.ShowAppreciation);
            d.SelectedPeriod = d.Periods[0]; // whole academic year
        });

        var p = Assert.Single(pages);
        Assert.Equal("Attestation de présence", p.Title);
        var text = Text(p);
        Assert.Contains(student.FullName, text);
        var attended = page.Attendance.Count(a => a.Status.Kind is BadgeKind.Ok or BadgeKind.Warn);
        Assert.Contains($"a assisté à {attended} séance(s) sur {page.Attendance.Count}", text);
        Assert.Contains("l'année scolaire 2026–2027", text);
        Assert.Contains("taux de présence", text);
    }

    [Fact]
    public async Task Group_report_cards_print_one_page_per_student_in_a_single_job()
    {
        await using var host = await UiHost.CreateAsync();
        var group = await GroupAsync(host, "Mathématiques · 3AS B");
        await host.Get<Navigator>().NavigateAsync<GradesViewModel>(group.Id);
        var grades = host.Page<GradesViewModel>();
        Assert.False(grades.HasError, grades.Error);
        var fake = host.Get<FakePlatform>();
        var jobs = fake.PrintedPages.Count;

        var run = grades.PrintReportCardsCommand.ExecuteAsync(null);
        var dialog = await DialogAsync<PrintDocumentsDialogViewModel>(host);
        Assert.True(dialog.IsGroupMode);
        Assert.False(dialog.ShowTypes);
        Assert.True(dialog.ShowAppreciation);
        Assert.Equal("Bulletins du groupe", dialog.Title);
        Assert.Equal(group.FullName, dialog.SubjectName);
        dialog.Appreciation = "Bon travail d'ensemble.";
        await dialog.ConfirmCommand.ExecuteAsync(null);
        await run;
        Assert.False(dialog.HasError, dialog.Error);

        Assert.Equal(jobs + 1, fake.PrintedPages.Count);
        var (job, pages) = fake.PrintedPages[^1];
        Assert.Contains(group.FullName, job);
        Assert.Equal(grades.GroupAverages.Count, pages.Count);
        Assert.Equal(pages.Count, dialog.PrintedPageCount);
        Assert.True(pages.Count > 1);
        var texts = pages.Select(Text).ToList();
        foreach (var row in grades.GroupAverages)
        {
            var t = Assert.Single(texts, x => x.Contains("Élève: " + row.Name + "\n"));
            Assert.Contains("Mathématiques · 3AS (B)", t);
            Assert.Contains($"/ {pages.Count} |", t); // rank among the group
            Assert.Contains(row.Average.Replace('.', ',') + "/20", t);
            Assert.Contains("Bon travail d'ensemble.", t);
        }
        Assert.All(pages, p => Assert.Equal("Bulletin de notes", p.Title));
        Assert.Contains(texts, t => t.Contains("1er / "));
    }

    [Fact]
    public void Mentions_follow_the_grading_scale()
    {
        Assert.Equal("Très bien", Mentions.For(16m, 20, 10));
        Assert.Equal("Bien", Mentions.For(15.99m, 20, 10));
        Assert.Equal("Assez bien", Mentions.For(12m, 20, 10));
        Assert.Equal("Passable", Mentions.For(10m, 20, 10));
        Assert.Equal("Insuffisant", Mentions.For(9.99m, 20, 10));
        Assert.Equal("—", Mentions.For(null, 20, 10));
        // On a /10 scale thresholds are halved.
        Assert.Equal("Très bien", Mentions.For(8m, 10, 5));
        Assert.Equal("Passable", Mentions.For(5.5m, 10, 5));
    }

    [Fact]
    public void Periods_places_and_dates_are_formatted_in_french()
    {
        var year = DocumentPeriod.AcademicYear("2026–2027", new DateTime(2026, 9, 26));
        Assert.Equal(new DateTime(2026, 9, 1), year.From);
        Assert.Equal(new DateTime(2027, 8, 31), year.To);
        Assert.Equal(2025, DocumentPeriod.StartYear(null, new DateTime(2026, 3, 1)));
        Assert.Equal("Février 2027", DocumentPeriod.Month(new DateTime(2027, 2, 14)).Label);
        Assert.Equal(new DateTime(2027, 2, 28), DocumentPeriod.Month(new DateTime(2027, 2, 14)).To);

        Assert.Equal("Alger", AcademicDocumentPages.City("12 rue Didouche Mourad, 16000 Alger"));
        Assert.Equal("Aïn-Témouchent", AcademicDocumentPages.City("Cité 200 logements, Aïn-Témouchent, Algérie"));
        Assert.Null(AcademicDocumentPages.City("  "));
        Assert.Equal("1er octobre 2026", AcademicDocumentPages.LongDate(new DateTime(2026, 10, 1)));
        Assert.Equal("Fait le 26 septembre 2026", AcademicDocumentPages.PlaceAndDate(new CenterSettings(), new DateTime(2026, 9, 26)));
        Assert.Equal("14,3", AcademicDocumentPages.Number(14.25m, 1));
    }

    [Fact]
    public void Certificate_wording_agrees_with_the_student_gender()
    {
        var student = new StudentIdentity(1, "Amira Haddad", "E1002", "3AS", "Lycée Ibn Khaldoun", new DateTime(2008, 3, 1), Gender.Female, "Mme Haddad", null, new DateTime(2025, 9, 15));
        var data = new CertificateData(student, "2026–2027", [new CertificateCourse("Mathématiques · 3AS", "A", "Karima Boudiaf", new DateTime(2025, 9, 15))], new DateTime(2026, 9, 26));
        var text = Text(AcademicDocumentPages.Certificate(data, new CenterSettings { Address = "Bab Ezzouar, Alger" }));
        Assert.Contains("née le 1er mars 2008", text);
        Assert.Contains("est inscrite", text);
        Assert.Contains("l'intéressée", text);
        Assert.Contains("Mathématiques · 3AS | A | Karima Boudiaf", text);
        Assert.Contains("Fait à Alger, le 26 septembre 2026", text);

        var empty = new ReportCardData(student, DocumentPeriod.Month(new DateTime(2026, 9, 1)), "2026–2027", [], null, AttendanceSummary.Empty, 20, 10, 2);
        var card = Text(AcademicDocumentPages.ReportCard(empty, new CenterSettings(), null, new DateTime(2026, 9, 26)));
        Assert.Contains("Aucun groupe suivi", card);
        Assert.Contains("Aucune séance relevée", card);
    }
}
