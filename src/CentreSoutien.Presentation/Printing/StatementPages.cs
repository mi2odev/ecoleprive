using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Presentation.Core;

namespace CentreSoutien.Presentation.Printing;

/// <summary>
/// "Relevé de compte" of a student, for the parents: group by group, the packs of sessions billed and what was paid,
/// then every receipt. Needs the student with payments (and their group), discount, enrollments → group → sessions.
/// </summary>
public static class StatementPages
{
    public static PrintPage Build(Student s, DateTime now)
    {
        var lines = Billing.Allocate(s, now);
        var accounts = Billing.Accounts(s, now);
        var blocks = new List<PrintBlock>
        {
            new PrintParagraph($"Le {now:dd/MM/yyyy}", Muted: true, AlignRight: true),
            new PrintFields(
            [
                ("Élève", $"{s.FullName} ({s.Matricule})"),
                ("Niveau", string.IsNullOrWhiteSpace(s.Level) ? "—" : s.Level),
                ("Parent / tuteur", s.Parent?.FullName ?? "—"),
                ("Remise", s.Discount is { IsActive: true } d ? d.ToString() : "Aucune"),
            ]),
            new PrintSpacer(8),
        };

        foreach (var a in accounts)
        {
            var mine = lines.Where(l => l.Charge.GroupId == a.GroupId).ToList();
            var status = a.Status is { } st ? $"séance {Math.Min(st.Done + 1, st.Size)}/{st.Size} du paquet {st.Pack}" : "a quitté le groupe";
            var size = s.Enrollments.First(e => e.GroupId == a.GroupId).Group!.SessionsPerPack;
            blocks.Add(new PrintTableBlock(new PrintTable($"{a.Group} · {Money.Format(a.PackPrice)} les {size} séances · {status}",
                ["Paquet", "Début", "Montant", "Payé", "Reste"],
                mine.Select(l => (IReadOnlyList<string>)
                [
                    $"n° {l.Charge.Pack}", l.Charge.Date.ToString("dd/MM/yyyy"), Money.Format(l.Charge.Amount), Money.Format(l.Paid), Money.Format(l.Rest),
                ]).ToList(), [2, 3, 4])));
            blocks.Add(new PrintParagraph(
                a.Balance > 0 ? $"Reste à payer pour ce groupe : {Money.Format(a.Balance)}"
                : a.Credit > 0 ? $"À jour · {Money.Format(a.Credit)} payés d'avance" : "À jour", Bold: true, AlignRight: true));
        }
        if (accounts.Count == 0) blocks.Add(new PrintParagraph("Aucun groupe.", Muted: true));

        var receipts = s.Payments.Where(p => !p.IsCancelled).OrderBy(p => p.Date).ToList();
        blocks.Add(new PrintSpacer(8));
        blocks.Add(new PrintTableBlock(new PrintTable("Paiements reçus", ["Date", "Reçu", "Objet", "Mode", "Montant"],
            receipts.Select(p => (IReadOnlyList<string>)
            [
                p.Date.ToString("dd/MM/yyyy"), p.ReceiptNumber, p.Group?.FullName ?? Labels.Of(p.Kind), Labels.Of(p.Method), Money.Format(p.Amount),
            ]).ToList(), [4])));

        var balance = accounts.Sum(a => a.Balance);
        blocks.Add(new PrintSpacer(8));
        blocks.Add(new PrintFields(
        [
            ("Total facturé (séances)", Money.Format(lines.Sum(l => l.Charge.Amount))),
            ("Total payé (séances)", Money.Format(receipts.Where(p => p.Kind == PaymentKind.Sessions).Sum(p => p.Amount))),
            ("Reste à payer", Money.Format(balance)),
        ]));
        blocks.Add(new PrintParagraph(balance > 0
            ? "Merci de régler le reste à payer au secrétariat du centre."
            : "Le compte de l'élève est à jour. Merci de votre confiance.", Muted: true));
        blocks.Add(new PrintSpacer(20));
        blocks.Add(new PrintSignature("La direction"));
        return new PrintPage("Relevé de compte", s.FullName, blocks);
    }
}
