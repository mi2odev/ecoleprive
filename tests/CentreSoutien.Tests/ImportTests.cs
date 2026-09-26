using ClosedXML.Excel;
using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Infrastructure.Data;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CentreSoutien.Presentation.ViewModels.Pages;
using CentreSoutien.Tests.Presentation;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Tests;

public class ImportTests
{
    private sealed record Setup(int MathGroupId, int PhysicsGroupId, int KarimId, int DiscountId);

    /// <summary>Math 3AS A (5 places), Physique 3AS A (1 place, taken by Sami Haddad), parent Karim Haddad, discount "Fratrie".</summary>
    private static async Task<Setup> SeedAsync(TestHost host)
    {
        var subjects = host.Get<ICrudService<Subject>>();
        var math = await subjects.SaveAsync(new Subject { Name = "Mathématiques", ShortName = "Maths" });
        var physics = await subjects.SaveAsync(new Subject { Name = "Physique" });
        var courses = host.Get<ICourseService>();
        var mathCourse = await courses.SaveAsync(new Course { SubjectId = math.Id, Level = "3AS", MonthlyPrice = 4500 });
        var physicsCourse = await courses.SaveAsync(new Course { SubjectId = physics.Id, Level = "3AS", MonthlyPrice = 4000 });
        var groups = host.Get<IGroupService>();
        var mathA = await groups.SaveAsync(new Group { CourseId = mathCourse.Id, Name = "A", Capacity = 5 }, []);
        var physicsA = await groups.SaveAsync(new Group { CourseId = physicsCourse.Id, Name = "A", Capacity = 1 }, []);
        var discount = await host.Get<ICrudService<Discount>>().SaveAsync(new Discount { Name = "Fratrie", Type = DiscountType.Percent, Value = 10 });
        var karim = await host.Get<IParentService>().SaveAsync(new Parent { FullName = "Karim Haddad", Phone = "0550 12 34 56", Relation = "Père" });
        var students = host.Get<IStudentService>();
        var sami = await students.SaveAsync(new Student { FirstName = "Sami", LastName = "Haddad", Level = "3AS", BirthDate = new DateTime(2008, 5, 1), ParentId = karim.Id });
        await students.EnrollAsync(sami.Id, physicsA.Id);
        return new Setup(mathA.Id, physicsA.Id, karim.Id, discount.Id);
    }

    /// <summary>Columns in another order than the template, an extra column, text and Excel dates, a blank row.</summary>
    private static string WriteWorkbook(string folder)
    {
        var path = Path.Combine(folder, "eleves.xlsx");
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Liste");
        object?[][] rows =
        [
            ["Prénom", "NOM", "Niveau", "Date de naissance", "Colonne perso", "Parent (nom)", "Téléphone parent", "Groupes", "Remise", "Sexe (G/F)"],
            ["Amel", "Bénali", "3AS", "15/04/2008", "x", "Farid Bénali", "0770 11 22 33", "Mathématiques 3AS A", "fratrie", "F"],   // 2: OK
            ["Yanis", "Bénali", "3AS", new DateTime(2010, 2, 1), null, "Farid Benali", "0770112233", "Maths 3AS A; Physique 3AS A", null, "G"], // 3: full group
            ["Lina", null, "3AS", null, null, null, null, null, null, "F"],                                                                // 4: no last name
            ["Nadia", "Haddad", "3AS", new DateTime(2009, 6, 10), null, "M. Haddad", "0550-12-34-56", "Chimie 3AS B", "Remise inconnue", "F"], // 5: unknown group + discount
            ["Sami", "Haddad", "3AS", "01/05/2008", null, "Karim Haddad", "0550 12 34 56", null, null, "G"],                               // 6: duplicate
            ["Omar", "Kaci", "3AS", "31/02/2008", null, null, null, null, null, "G"],                                                      // 7: bad date
            [null, null, null, null, null, null, null, null, null, null],                                                                  // 8: blank
            ["Inès", "Toumi", "3 as", null, null, "Mme Toumi", null, "Mathématiques · 3AS A", null, null],                                 // 9: OK
        ];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
            {
                var cell = ws.Cell(r + 1, c + 1);
                switch (rows[r][c])
                {
                    case DateTime d: cell.Value = d; break;
                    case string s: cell.Value = s; break;
                }
            }
        wb.SaveAs(path);
        return path;
    }

    [Fact]
    public async Task Template_has_the_columns_an_example_and_a_help_sheet_listing_groups_and_discounts()
    {
        await using var host = await TestHost.CreateAsync(encrypt: false);
        await SeedAsync(host);
        var path = Path.Combine(host.Folder, "modele.xlsx");
        await host.Get<IImportService>().CreateTemplateAsync(path);

        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet(1);
        var headers = Enumerable.Range(1, 14).Select(c => ws.Cell(1, c).GetString()).ToList();
        Assert.Equal(["Nom", "Prénom", "Niveau", "Date de naissance", "Sexe (G/F)", "Établissement", "Téléphone élève", "Adresse",
            "Parent (nom)", "Téléphone parent", "Lien (Père/Mère/Tuteur)", "Groupes", "Remise", "Notes"], headers);
        Assert.False(ws.Cell(2, 1).IsEmpty()); // example row
        Assert.Contains("Mathématiques 3AS A", ws.Cell(2, 12).GetString());

        var help = wb.Worksheet("Aide");
        var text = string.Join("\n", help.CellsUsed().Select(c => c.GetFormattedString()));
        Assert.Contains("Mathématiques 3AS A", text);
        Assert.Contains("Physique 3AS A", text);
        Assert.Contains("Fratrie", text);
        Assert.Contains("3AS", text);

        // The template itself previews cleanly: its example row is a valid student.
        var preview = await host.Get<IImportService>().PreviewAsync(path);
        var row = Assert.Single(preview.Rows);
        Assert.NotEqual(ImportRowStatus.Error, row.Status);
    }

    [Fact]
    public async Task Preview_reports_ok_warning_and_error_rows()
    {
        await using var host = await TestHost.CreateAsync(encrypt: false);
        await SeedAsync(host);
        var preview = await host.Get<IImportService>().PreviewAsync(WriteWorkbook(host.Folder));

        Assert.Equal([2, 3, 4, 5, 6, 7, 9], preview.Rows.Select(r => r.Data.RowNumber));
        ImportRow Row(int n) => preview.Rows.Single(r => r.Data.RowNumber == n);

        Assert.Equal(ImportRowStatus.Ok, Row(2).Status);
        Assert.Equal(new DateTime(2008, 4, 15), Row(2).Data.BirthDate);
        Assert.Equal(Gender.Female, Row(2).Data.Gender);
        Assert.Equal("Fratrie", Row(2).MatchedDiscount);
        Assert.Equal(["Mathématiques · 3AS A"], Row(2).MatchedGroups);
        Assert.StartsWith("Nouveau parent", Row(2).ParentAction);

        Assert.Equal(ImportRowStatus.Warning, Row(3).Status);
        Assert.Equal(new DateTime(2010, 2, 1), Row(3).Data.BirthDate);
        Assert.Contains(Row(3).Messages, m => m.Contains("Groupe complet"));
        Assert.Equal(["Mathématiques · 3AS A"], Row(3).MatchedGroups);
        Assert.Contains("ligne précédente", Row(3).ParentAction); // sibling: same parent as row 2 (matched by phone)

        Assert.Equal(ImportRowStatus.Error, Row(4).Status);
        Assert.Contains("Nom manquant", Row(4).Messages);

        Assert.Equal(ImportRowStatus.Warning, Row(5).Status);
        Assert.Contains(Row(5).Messages, m => m.Contains("Groupe introuvable") && m.Contains("Chimie"));
        Assert.Contains(Row(5).Messages, m => m.Contains("Remise introuvable"));
        Assert.Equal("Parent existant : Karim Haddad", Row(5).ParentAction);

        Assert.Equal(ImportRowStatus.Warning, Row(6).Status);
        Assert.True(Row(6).IsDuplicate);
        Assert.Contains(Row(6).Messages, m => m.Contains("Doublon"));

        Assert.Equal(ImportRowStatus.Error, Row(7).Status);
        Assert.Contains(Row(7).Messages, m => m.Contains("Date de naissance illisible"));

        Assert.Equal(ImportRowStatus.Ok, Row(9).Status);
        Assert.Equal("3AS", Row(9).Data.Level);

        Assert.Equal(2, preview.OkCount);
        Assert.Equal(3, preview.WarningCount);
        Assert.Equal(2, preview.ErrorCount);
        Assert.Equal(4, preview.ImportableCount(skipDuplicates: true));
        Assert.Equal(5, preview.ImportableCount(skipDuplicates: false));

        // Nothing was written by the preview.
        Assert.Single(await host.Get<IStudentService>().ListAsync(DateTime.Today));
    }

    [Fact]
    public async Task Import_creates_students_matches_parents_by_phone_and_enrolls_them()
    {
        await using var host = await TestHost.CreateAsync(encrypt: false);
        var setup = await SeedAsync(host);
        var import = host.Get<IImportService>();
        var preview = await import.PreviewAsync(WriteWorkbook(host.Folder));

        var result = await import.ImportAsync(preview, new ImportOptions(SkipDuplicates: true));

        Assert.Equal(4, result.StudentsCreated);
        Assert.Equal(2, result.ParentsCreated); // Farid Bénali (shared by two children) and Mme Toumi
        Assert.Equal(3, result.Enrollments);    // Amel, Yanis and Inès in Math A; Physique A is full
        Assert.Equal(3, result.Skipped);        // missing name, bad date, duplicate
        Assert.Contains(result.Messages, m => m.StartsWith("Ligne 4 ignorée"));
        Assert.Contains(result.Messages, m => m.StartsWith("Ligne 6 ignorée (doublon)"));

        await using var db = await host.Get<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var students = await db.Students.Include(s => s.Parent).Include(s => s.Enrollments).ToListAsync();
        Assert.Equal(5, students.Count);
        Assert.Equal(students.Count, students.Select(s => s.Matricule).Distinct().Count());
        Assert.Equal(["E1001", "E1002", "E1003", "E1004", "E1005"], students.Select(s => s.Matricule).Order());

        var amel = students.Single(s => s.FirstName == "Amel");
        var yanis = students.Single(s => s.FirstName == "Yanis");
        var nadia = students.Single(s => s.FirstName == "Nadia");
        var ines = students.Single(s => s.FirstName == "Inès");
        Assert.Equal("Bénali", amel.LastName);
        Assert.Equal(amel.ParentId, yanis.ParentId);
        Assert.Equal("Farid Bénali", amel.Parent!.FullName);
        Assert.Equal(setup.KarimId, nadia.ParentId);    // matched by phone although the name differs
        Assert.Equal("Mme Toumi", ines.Parent!.FullName);
        Assert.Equal(setup.DiscountId, amel.DiscountId);
        Assert.Null(nadia.DiscountId);
        Assert.Equal(new DateTime(2026, 9, 26), amel.EnrolledOn);

        Assert.Equal([setup.MathGroupId], amel.Enrollments.Select(e => e.GroupId));
        Assert.Equal([setup.MathGroupId], yanis.Enrollments.Select(e => e.GroupId));
        Assert.Empty(nadia.Enrollments);
        Assert.Equal(1, await db.Enrollments.CountAsync(e => e.GroupId == setup.PhysicsGroupId));
        Assert.Equal(3, await db.Parents.CountAsync());
    }

    [Fact]
    public async Task Import_from_file_can_keep_duplicates()
    {
        await using var host = await TestHost.CreateAsync(encrypt: false);
        await SeedAsync(host);
        var result = await host.Get<IImportService>().ImportAsync(WriteWorkbook(host.Folder), new ImportOptions(SkipDuplicates: false));
        Assert.Equal(5, result.StudentsCreated);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(2, (await host.Get<IStudentService>().ListAsync(DateTime.Today)).Count(s => s.FullName == "Sami Haddad"));
    }

    [Fact]
    public async Task Preview_of_a_file_without_headers_is_refused()
    {
        await using var host = await TestHost.CreateAsync(encrypt: false);
        var path = Path.Combine(host.Folder, "vide.xlsx");
        using (var wb = new XLWorkbook())
        {
            wb.Worksheets.Add("A").Cell(1, 1).Value = "Bonjour";
            wb.SaveAs(path);
        }
        var ex = await Assert.ThrowsAsync<BusinessException>(() => host.Get<IImportService>().PreviewAsync(path));
        Assert.Contains("Nom", ex.Message);
    }

    [Fact]
    public async Task Owner_imports_students_from_the_students_page()
    {
        await using var host = await UiHost.CreateAsync(demo: false);
        await SeedAsync(host);
        var fake = host.Get<FakePlatform>();
        var nav = host.Get<Navigator>();
        await nav.NavigateAsync<StudentsViewModel>();
        var page = host.Page<StudentsViewModel>();
        Assert.Single(page.Rows);

        var importTask = page.ImportCommand.ExecuteAsync(null);
        var dialog = Assert.IsType<ImportStudentsDialogViewModel>(host.Get<DialogHost>().Current);
        Assert.Equal(900, dialog.Width);

        fake.NextSaveFile = Path.Combine(host.Folder, "modele.xlsx");
        await dialog.DownloadTemplateCommand.ExecuteAsync(null);
        Assert.True(File.Exists(fake.NextSaveFile));
        Assert.Contains(fake.NextSaveFile, fake.Opened);

        await dialog.ConfirmCommand.ExecuteAsync(null); // no file yet
        Assert.True(dialog.HasError);

        fake.NextOpenFile = WriteWorkbook(host.Folder);
        await dialog.PickFileCommand.ExecuteAsync(null);
        Assert.False(dialog.HasError, dialog.Error);
        Assert.True(dialog.HasPreview);
        Assert.Equal(7, dialog.Lines.Count);
        Assert.Equal("Importer 4 élèves", dialog.ConfirmText);
        Assert.Equal("Ignorée", dialog.Lines.Single(l => l.Line == 6).Status.Text);
        Assert.Equal("Erreur", dialog.Lines.Single(l => l.Line == 4).Status.Text);
        dialog.SkipDuplicates = false;
        Assert.Equal("Importer 5 élèves", dialog.ConfirmText);
        dialog.SkipDuplicates = true;

        await dialog.ConfirmCommand.ExecuteAsync(null);
        await importTask;
        Assert.False(dialog.HasError, dialog.Error);
        Assert.Null(host.Get<DialogHost>().Current);
        Assert.Equal(4, dialog.Result!.StudentsCreated);
        Assert.Equal(5, page.Rows.Count);
        Assert.Contains("4 élèves importés", host.Get<Notifier>().Message);
    }
}
