using System.Windows;
using System.Windows.Media;
using CentreSoutien.Presentation.Core;

namespace CentreSoutien.Desktop.Controls;

/// <summary>
/// Grouped bar chart. Vertical by default (categories along the bottom), with series of kind
/// <see cref="ChartSeriesKind.Line"/> drawn as a line over the bars; <see cref="Horizontal"/> draws one row per category.
/// </summary>
public sealed class BarChart : ChartBase
{
    public static readonly DependencyProperty HorizontalProperty = DependencyProperty.Register(
        nameof(Horizontal), typeof(bool), typeof(BarChart), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool Horizontal { get => (bool)GetValue(HorizontalProperty); set => SetValue(HorizontalProperty, value); }

    protected override bool IsLine(ChartSeries series) => !Horizontal && series.Kind == ChartSeriesKind.Line;

    protected override void RenderPlot(DrawingContext dc, ChartData data, Rect area)
    {
        if (Horizontal) RenderHorizontal(dc, data, area);
        else RenderVertical(dc, data, area);
    }

    /// <summary>One row per category: label on the left, bars growing to the right with their value at the end.</summary>
    private void RenderHorizontal(DrawingContext dc, ChartData data, Rect area)
    {
        var n = data.Categories.Count;
        var (_, max) = Extent(data);
        // Bars are proportional to the largest value; all-zero data keeps a unit scale.
        var hi = max > 0 ? max : 1;

        var labels = data.Categories.Select(c => Text(c, AxisFontSize, MutedBrush)).ToList();
        var labelW = Math.Min(labels.Max(l => l.Width) + 12, area.Width * 0.4);
        var valueW = data.Series.SelectMany(s => s.Values).Where(v => !double.IsNaN(v))
            .Select(v => Text(data.FormatAxis(v), AxisFontSize, MutedBrush).Width).DefaultIfEmpty(0).Max() + 8;
        var plot = new Rect(area.Left + labelW, area.Top, Math.Max(1, area.Width - labelW - valueW), area.Height);
        double X(double v) => plot.Left + Math.Max(0, v) / hi * plot.Width;

        var rh = plot.Height / n;
        var count = data.Series.Count;
        var groupH = Math.Min(rh * 0.72, count * 14);
        var barH = groupH / count;
        var gap = barH > 6 ? 2 : 0;

        for (var i = 0; i < n; i++)
        {
            var row = new Rect(area.Left, area.Top + i * rh, area.Width, rh);
            Bands.Add(row);
            if (i == Hover) dc.DrawRoundedRectangle(HoverFill, null, new Rect(row.Left, row.Top + 1, row.Width, Math.Max(0, row.Height - 2)), 4, 4);

            var label = i == Hover ? Text(data.Categories[i], AxisFontSize, InkBrush, strong: true) : labels[i];
            dc.DrawText(label, new Point(Math.Max(area.Left, plot.Left - 10 - label.Width), row.Top + (rh - label.Height) / 2));

            var y0 = row.Top + (rh - groupH) / 2;
            for (var k = 0; k < count; k++)
            {
                var v = ValueAt(data.Series[k], i);
                if (double.IsNaN(v)) continue;
                var rect = new Rect(plot.Left, y0 + k * barH + gap / 2.0, Math.Max(v > 0 ? 2 : 0, X(v) - plot.Left), Math.Max(1, barH - gap));
                if (rect.Width > 0) dc.DrawRoundedRectangle(BrushOf(data.Series[k].Color), null, rect, Math.Min(3, rect.Height / 2), Math.Min(3, rect.Height / 2));
                var ft = Text(data.FormatAxis(v), AxisFontSize - 0.5, i == Hover ? InkBrush : MutedBrush);
                if (ft.Height <= rh + 2) dc.DrawText(ft, new Point(rect.Right + 5, rect.Top + (rect.Height - ft.Height) / 2));
            }
        }

        // Baseline.
        var x = Math.Round(plot.Left) + 0.5;
        dc.DrawLine(new Pen(LineBrush, 1), new Point(x, area.Top), new Point(x, area.Bottom));
    }
}
