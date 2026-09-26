using ClosedXML.Excel;
using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Services;

public sealed class ExportService(IDbContextFactory<AppDbContext> factory) : IExportService
{
    public async Task ExportAllAsync(string path, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        using var wb = new XLWorkbook();

        var students = await db.StudentsForBilling().ToListAsync(ct);
        var p = Period.Current;
        Sheet(wb, "Élèves", ["Matricule", "Nom", "Prénom", "Niveau", "Établissement", "Date de naissance", "Téléphone", "Parent", "Téléphone parent", "Actif", "Inscrit le", "Remise", "Mensualité", "Reste du mois"],
            students.OrderBy(s => s.LastName).Select(s => Row(s.Matricule, s.LastName, s.FirstName, s.Level, s.School, s.BirthDate, s.Phone, s.Parent?.FullName, s.Parent?.Phone,
                s.IsActive ? "Oui" : "Non", s.EnrolledOn, s.Discount?.Name, Billing.MonthlyDue(s, p), Billing.Balance(s, p))));

        var parents = await db.Parents.AsNoTracking().Include(x => x.Children).ToListAsync(ct);
        Sheet(wb, "Parents", ["Nom", "Lien", "Téléphone", "Téléphone 2", "E-mail", "Adresse", "Enfants"],
            parents.OrderBy(x => x.FullName).Select(x => Row(x.FullName, x.Relation, x.Phone, x.Phone2, x.Email, x.Address, string.Join(", ", x.Children.Select(c => c.FullName)))));

        var teachers = await db.Teachers.AsNoTracking().Include(t => t.Subject).ToListAsync(ct);
        Sheet(wb, "Enseignants", ["Nom", "Prénom", "Matière", "Téléphone", "E-mail", "Depuis", "Rémunération", "Valeur", "Actif"],
            teachers.OrderBy(t => t.LastName).Select(t => Row(t.LastName, t.FirstName, t.Subject?.Name, t.Phone, t.Email, t.StartYear, Labels.Of(t.CompensationType), t.CompensationValue, t.IsActive ? "Oui" : "Non")));

        var groups = await db.GroupsFull().ToListAsync(ct);
        Sheet(wb, "Groupes", ["Matière · niveau", "Groupe", "Enseignant", "Salle", "Capacité", "Inscrits", "Horaire", "Prix / mois"],
            groups.OrderBy(g => g.FullName).Select(g => Row(g.SubjectLevel, g.Name, g.Teacher?.FullName, g.Room?.Name, g.Capacity,
                g.Enrollments.Count(e => e.IsActiveOn(DateTime.Today)), Labels.Slots(g.Slots), g.MonthlyPrice)));

        var pays = await db.StudentPayments.AsNoTracking().Include(x => x.Student).ToListAsync(ct);
        Sheet(wb, "Paiements élèves", ["Reçu", "Date", "Élève", "Matricule", "Type", "Mois", "Mode", "Montant", "Note"],
            pays.OrderByDescending(x => x.Date).Select(x => Row(x.ReceiptNumber, x.Date, x.Student?.FullName, x.Student?.Matricule, Labels.Of(x.Kind), Labels.Month(x.Period), Labels.Of(x.Method), x.Amount, x.Note)));

        var tpays = await db.TeacherPayments.AsNoTracking().Include(x => x.Teacher).ToListAsync(ct);
        Sheet(wb, "Paiements enseignants", ["Date", "Enseignant", "Mois", "Mode", "Montant", "Note"],
            tpays.OrderByDescending(x => x.Date).Select(x => Row(x.Date, x.Teacher?.FullName, Labels.Month(x.Period), Labels.Of(x.Method), x.Amount, x.Note)));

        var expenses = await db.Expenses.AsNoTracking().ToListAsync(ct);
        Sheet(wb, "Dépenses", ["Date", "Catégorie", "Description", "Fournisseur", "Mode", "Montant"],
            expenses.OrderByDescending(x => x.Date).Select(x => Row(x.Date, x.Category, x.Description, x.Supplier, Labels.Of(x.Method), x.Amount)));

        var att = await db.Attendance.AsNoTracking().Include(a => a.Student).Include(a => a.Session).ThenInclude(s => s!.Group).ThenInclude(g => g!.Subject).AsSplitQuery().ToListAsync(ct);
        Sheet(wb, "Présences", ["Date", "Heure", "Groupe", "Élève", "Statut"],
            att.OrderByDescending(a => a.Session!.Date).Select(a => Row(a.Session!.Date, Labels.Time(a.Session.Start), a.Session.Group?.FullName, a.Student?.FullName, Labels.Of(a.Status))));

        var grades = await db.Grades.AsNoTracking().Include(g => g.Student).Include(g => g.Exam).ThenInclude(e => e!.Group).ThenInclude(g => g!.Subject).AsSplitQuery().ToListAsync(ct);
        Sheet(wb, "Notes", ["Date", "Groupe", "Évaluation", "Élève", "Note", "Sur", "Coefficient", "Commentaire"],
            grades.OrderByDescending(g => g.Exam!.Date).Select(g => Row(g.Exam!.Date, g.Exam.Group?.FullName, g.Exam.Title, g.Student?.FullName, g.Score, g.Exam.MaxScore, g.Exam.Coefficient, g.Comment)));

        wb.SaveAs(path);
    }

    public Task ExportTableAsync(string path, string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook();
        Sheet(wb, sheetName, headers, rows);
        wb.SaveAs(path);
        return Task.CompletedTask;
    }

    private static IReadOnlyList<object?> Row(params object?[] values) => values;

    private static void Sheet(XLWorkbook wb, string name, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var ws = wb.Worksheets.Add(name.Length > 31 ? name[..31] : name);
        for (var c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EBEFF4");
        }
        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Count; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (row[c])
                {
                    case null: break;
                    case DateTime d:
                        cell.Value = d;
                        cell.Style.DateFormat.Format = d.TimeOfDay == TimeSpan.Zero ? "dd/mm/yyyy" : "dd/mm/yyyy hh:mm";
                        break;
                    case decimal m:
                        cell.Value = m;
                        cell.Style.NumberFormat.Format = "#,##0";
                        break;
                    case int i: cell.Value = i; break;
                    case double f: cell.Value = f; break;
                    default: cell.Value = row[c]!.ToString(); break;
                }
            }
            r++;
        }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(r, 200));
    }
}
