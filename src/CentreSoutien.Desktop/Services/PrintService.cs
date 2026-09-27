using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Presentation.Core;

namespace CentreSoutien.Desktop.Services;

/// <summary>
/// Prints receipts and reports with WPF flow documents. Choosing the "Microsoft Print to PDF" printer
/// in the print dialog produces a PDF file.
/// </summary>
public sealed class PrintService : IPrintService
{
    private static readonly FontFamily Sans = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Public Sans");
    private static readonly FontFamily Serif = new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Newsreader Display");
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x13, 0x1A, 0x2A));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x5B, 0x64, 0x75));
    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(0xDC, 0xE2, 0xEA));

    public void PrintReceipt(StudentPayment payment, CenterSettings settings, string? logoPath)
    {
        var doc = NewDocument();
        doc.Blocks.Add(Header(settings, settings.ShowLogoOnReceipt ? logoPath : null));

        var title = new Paragraph { Margin = new Thickness(0, 18, 0, 4) };
        title.Inlines.Add(new Run("Reçu de paiement") { FontFamily = Serif, FontSize = 24 });
        doc.Blocks.Add(title);
        doc.Blocks.Add(new Paragraph(new Run($"N° {payment.ReceiptNumber} · {payment.Date:dd/MM/yyyy HH:mm}")) { Foreground = Muted, Margin = new Thickness(0, 0, 0, 14) });

        var student = payment.Student;
        var table = KeyValueTable(
        [
            ("Élève", student is null ? "—" : $"{student.FullName} ({student.Matricule})"),
            ("Parent / tuteur", student?.Parent?.FullName ?? "—"),
            ("Objet", payment.Kind == Domain.Enums.PaymentKind.Sessions
                ? payment.Group is { } g ? $"Séances · {g.FullName} ({g.SessionsPerPack} séances)" : "Paiement des séances"
                : Labels.Of(payment.Kind)),
            ("Mode de paiement", Labels.Of(payment.Method)),
            ("Note", string.IsNullOrWhiteSpace(payment.Note) ? "—" : payment.Note!),
        ]);
        doc.Blocks.Add(table);

        var amount = new Paragraph { Margin = new Thickness(0, 18, 0, 0), BorderBrush = Ink, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 12, 0, 0) };
        amount.Inlines.Add(new Run("Montant reçu   ") { Foreground = Muted });
        amount.Inlines.Add(new Run(Money.Format(payment.Amount)) { FontFamily = Serif, FontSize = 26 });
        doc.Blocks.Add(amount);

        var sign = new Paragraph(new Run("Cachet et signature")) { Margin = new Thickness(0, 36, 0, 40), TextAlignment = TextAlignment.Right, Foreground = Muted };
        doc.Blocks.Add(sign);
        if (!string.IsNullOrWhiteSpace(settings.ReceiptFooter))
            doc.Blocks.Add(new Paragraph(new Run(settings.ReceiptFooter)) { FontSize = 11, Foreground = Muted, TextAlignment = TextAlignment.Center });

        Print(doc, "Reçu " + payment.ReceiptNumber);
    }

    public void PrintReport(string title, string subtitle, CenterSettings settings, IReadOnlyList<(string Label, string Value)> summary, IReadOnlyList<PrintTable> tables)
    {
        var doc = NewDocument();
        doc.Blocks.Add(Header(settings, null));
        var t = new Paragraph { Margin = new Thickness(0, 18, 0, 2) };
        t.Inlines.Add(new Run(title) { FontFamily = Serif, FontSize = 24 });
        doc.Blocks.Add(t);
        doc.Blocks.Add(new Paragraph(new Run(subtitle)) { Foreground = Muted, Margin = new Thickness(0, 0, 0, 12) });
        if (summary.Count > 0) doc.Blocks.Add(KeyValueTable(summary));

        foreach (var pt in tables)
        {
            doc.Blocks.Add(new Paragraph(new Run(pt.Title) { FontFamily = Serif, FontSize = 16 }) { Margin = new Thickness(0, 18, 0, 6) });
            var table = new Table { CellSpacing = 0, BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0) };
            foreach (var _ in pt.Headers) table.Columns.Add(new TableColumn());
            var group = new TableRowGroup();
            group.Rows.Add(Row(pt.Headers, pt.RightAligned, header: true));
            foreach (var r in pt.Rows) group.Rows.Add(Row(r, pt.RightAligned, header: false));
            table.RowGroups.Add(group);
            doc.Blocks.Add(table);
        }
        doc.Blocks.Add(new Paragraph(new Run($"Imprimé le {DateTime.Now:dd/MM/yyyy HH:mm}")) { FontSize = 10, Foreground = Muted, Margin = new Thickness(0, 24, 0, 0) });
        Print(doc, title);
    }

    public void PrintPages(string jobName, CenterSettings settings, string? logoPath, IReadOnlyList<PrintPage> pages)
    {
        if (pages.Count == 0) return;
        var doc = NewDocument();
        var first = true;
        foreach (var page in pages)
        {
            var blocks = new List<Block>();
            if (page.ShowHeader) blocks.Add(Header(settings, logoPath));
            var title = new Paragraph { Margin = new Thickness(0, 18, 0, 2) };
            title.Inlines.Add(new Run(page.Title) { FontFamily = Serif, FontSize = 24 });
            blocks.Add(title);
            if (!string.IsNullOrWhiteSpace(page.Subtitle))
                blocks.Add(new Paragraph(new Run(page.Subtitle)) { Foreground = Muted, Margin = new Thickness(0, 0, 0, 12) });
            foreach (var b in page.Blocks) blocks.Add(Render(b));

            // Each page starts on a new sheet.
            if (!first) blocks[0].BreakPageBefore = true;
            first = false;
            foreach (var b in blocks) doc.Blocks.Add(b);
        }
        Print(doc, jobName);
    }

    private static Block Render(PrintBlock block) => block switch
    {
        PrintHeading h => new Paragraph(new Run(h.Text) { FontFamily = Serif, FontSize = 16 }) { Margin = new Thickness(0, 16, 0, 6) },
        PrintParagraph p => new Paragraph(new Run(p.Text))
        {
            Foreground = p.Muted ? Muted : Ink,
            FontWeight = p.Bold ? FontWeights.SemiBold : FontWeights.Normal,
            FontSize = p.Size,
            TextAlignment = p.Center ? TextAlignment.Center : p.AlignRight ? TextAlignment.Right : TextAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8),
            LineHeight = p.Size * 1.5,
        },
        PrintFields f => KeyValueTable(f.Rows),
        PrintTableBlock t => DataTable(t.Table),
        PrintSpacer s => new Paragraph { Margin = new Thickness(0, s.Height, 0, 0), FontSize = 1 },
        PrintSignature sig => new Paragraph(new Run(sig.Label)) { Margin = new Thickness(0, 32, 0, 40), TextAlignment = TextAlignment.Right, Foreground = Muted },
        _ => new Paragraph(),
    };

    private static Table DataTable(PrintTable pt)
    {
        var table = new Table { CellSpacing = 0, BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0) };
        foreach (var _ in pt.Headers) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        group.Rows.Add(Row(pt.Headers, pt.RightAligned, header: true));
        foreach (var r in pt.Rows) group.Rows.Add(Row(r, pt.RightAligned, header: false));
        table.RowGroups.Add(group);
        return table;
    }

    private static FlowDocument NewDocument() => new()
    {
        FontFamily = Sans,
        FontSize = 12,
        Foreground = Ink,
        PagePadding = new Thickness(56),
        ColumnWidth = double.PositiveInfinity,
    };

    private static Block Header(CenterSettings s, string? logoPath)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var logo = LoadLogo(logoPath);
        if (logo is not null)
        {
            var img = new Image { Source = logo, Height = 64, Margin = new Thickness(0, 0, 16, 0) };
            grid.Children.Add(img);
        }
        var info = new StackPanel();
        info.Children.Add(new TextBlock { Text = s.CenterName, FontFamily = Serif, FontSize = 20, Foreground = Ink });
        foreach (var line in new[] { s.Address, string.Join(" · ", new[] { s.Phone, s.Email }.Where(x => !string.IsNullOrWhiteSpace(x))) })
            if (!string.IsNullOrWhiteSpace(line)) info.Children.Add(new TextBlock { Text = line, FontSize = 11, Foreground = Muted, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        return new BlockUIContainer(grid) { Padding = new Thickness(0, 0, 0, 12), BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    private static ImageSource? LoadLogo(string? path)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.UriSource = path is not null && File.Exists(path) ? new Uri(path) : new Uri("pack://application:,,,/Assets/logo.png");
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch
        {
            return null;
        }
    }

    private static Table KeyValueTable(IReadOnlyList<(string Label, string Value)> rows)
    {
        var table = new Table { CellSpacing = 0 };
        table.Columns.Add(new TableColumn { Width = new GridLength(170) });
        table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        foreach (var (label, value) in rows)
        {
            var row = new TableRow();
            row.Cells.Add(new TableCell(new Paragraph(new Run(label)) { Foreground = Muted }) { Padding = new Thickness(0, 5, 8, 5), BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) });
            row.Cells.Add(new TableCell(new Paragraph(new Run(value))) { Padding = new Thickness(0, 5, 0, 5), BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) });
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        return table;
    }

    private static TableRow Row(IReadOnlyList<string> cells, IReadOnlyList<int>? right, bool header)
    {
        var row = new TableRow();
        for (var i = 0; i < cells.Count; i++)
        {
            var p = new Paragraph(new Run(cells[i]))
            {
                TextAlignment = right?.Contains(i) == true ? TextAlignment.Right : TextAlignment.Left,
                Foreground = header ? Muted : Ink,
                FontWeight = header ? FontWeights.Medium : FontWeights.Normal,
                FontSize = header ? 10.5 : 11,
            };
            row.Cells.Add(new TableCell(p) { Padding = new Thickness(0, 4, 8, 4), BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) });
        }
        return row;
    }

    private static void Print(FlowDocument doc, string jobName)
    {
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;
        doc.PageWidth = dlg.PrintableAreaWidth;
        doc.PageHeight = dlg.PrintableAreaHeight;
        dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, jobName);
    }
}
