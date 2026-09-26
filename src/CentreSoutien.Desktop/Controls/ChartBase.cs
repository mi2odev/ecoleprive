using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CentreSoutien.Presentation.Core;

namespace CentreSoutien.Desktop.Controls;

/// <summary>
/// Base of the small dashboard charts: draws a <see cref="ChartData"/> with WPF drawing primitives (no chart package).
/// Handles the legend, the value scale, gridlines, category labels, hover highlight and tooltips.
/// Colours come from the theme brushes through resource references, so light and dark themes both work.
/// </summary>
public abstract class ChartBase : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(ChartData), typeof(ChartBase), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((ChartBase)d).ResetHover()));

    public static readonly DependencyProperty ShowLegendProperty = DependencyProperty.Register(
        nameof(ShowLegend), typeof(bool), typeof(ChartBase), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText), typeof(string), typeof(ChartBase), new FrameworkPropertyMetadata("Pas encore de données", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentBrushProperty = BrushProperty(nameof(AccentBrush));
    public static readonly DependencyProperty OkBrushProperty = BrushProperty(nameof(OkBrush));
    public static readonly DependencyProperty WarnBrushProperty = BrushProperty(nameof(WarnBrush));
    public static readonly DependencyProperty BadBrushProperty = BrushProperty(nameof(BadBrush));
    public static readonly DependencyProperty MutedBrushProperty = BrushProperty(nameof(MutedBrush));
    public static readonly DependencyProperty InkBrushProperty = BrushProperty(nameof(InkBrush));
    public static readonly DependencyProperty LineBrushProperty = BrushProperty(nameof(LineBrush));
    public static readonly DependencyProperty PanelBrushProperty = BrushProperty(nameof(PanelBrush));

    private static DependencyProperty BrushProperty(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(ChartBase), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    protected const double AxisFontSize = 11.5;
    protected const double LegendFontSize = 12;

    /// <summary>Hit areas of the categories, filled while rendering (columns or rows).</summary>
    protected readonly List<Rect> Bands = [];

    private readonly ToolTip _tip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
    private int _hover = -1;

    protected ChartBase()
    {
        SetResourceReference(AccentBrushProperty, "AccentBrush");
        SetResourceReference(OkBrushProperty, "OkBrush");
        SetResourceReference(WarnBrushProperty, "WarnBrush");
        SetResourceReference(BadBrushProperty, "BadBrush");
        SetResourceReference(MutedBrushProperty, "MutedBrush");
        SetResourceReference(InkBrushProperty, "InkBrush");
        SetResourceReference(LineBrushProperty, "LineBrush");
        SetResourceReference(PanelBrushProperty, "PanelBrush");
        SnapsToDevicePixels = true;
        MinHeight = 120;
        ToolTip = _tip;
        ToolTipService.SetInitialShowDelay(this, 100);
        ToolTipService.SetBetweenShowDelay(this, 0);
        ToolTipOpening += (_, e) => { if (_hover < 0 || Data is null) e.Handled = true; };
    }

    public ChartData? Data { get => (ChartData?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public bool ShowLegend { get => (bool)GetValue(ShowLegendProperty); set => SetValue(ShowLegendProperty, value); }
    public string EmptyText { get => (string)GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }
    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public Brush OkBrush { get => (Brush)GetValue(OkBrushProperty); set => SetValue(OkBrushProperty, value); }
    public Brush WarnBrush { get => (Brush)GetValue(WarnBrushProperty); set => SetValue(WarnBrushProperty, value); }
    public Brush BadBrush { get => (Brush)GetValue(BadBrushProperty); set => SetValue(BadBrushProperty, value); }
    public Brush MutedBrush { get => (Brush)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    public Brush InkBrush { get => (Brush)GetValue(InkBrushProperty); set => SetValue(InkBrushProperty, value); }
    public Brush LineBrush { get => (Brush)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public Brush PanelBrush { get => (Brush)GetValue(PanelBrushProperty); set => SetValue(PanelBrushProperty, value); }

    /// <summary>Index of the hovered category, or -1.</summary>
    protected int Hover => _hover;

    /// <summary>True when the series is drawn as a line rather than bars.</summary>
    protected abstract bool IsLine(ChartSeries series);

    /// <summary>Draws the plot (axes, series, labels) inside <paramref name="area"/> and fills <see cref="Bands"/>.</summary>
    protected abstract void RenderPlot(DrawingContext dc, ChartData data, Rect area);

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        // Transparent background so the whole surface receives mouse moves (hover and tooltips).
        dc.DrawRectangle(Brushes.Transparent, null, bounds);
        Bands.Clear();
        var data = Data;
        if (bounds.Width < 40 || bounds.Height < 40) return;
        if (data is null || data.Categories.Count == 0 || data.Series.Count == 0)
        {
            DrawCentered(dc, EmptyText, bounds);
            return;
        }

        var top = 0.0;
        if (ShowLegend) top = DrawLegend(dc, data, bounds.Width) + 10;
        var area = new Rect(0, top, bounds.Width, Math.Max(0, bounds.Height - top));
        if (area.Height < 30) return;
        RenderPlot(dc, data, area);
        if (data.IsEmpty) DrawCentered(dc, EmptyText, area);
    }

    // ---------- Shared drawing helpers ----------

    protected Brush BrushOf(ChartColor color) => color switch
    {
        ChartColor.Ok => OkBrush,
        ChartColor.Warn => WarnBrush,
        ChartColor.Bad => BadBrush,
        ChartColor.Muted => MutedBrush,
        ChartColor.Ink => InkBrush,
        _ => AccentBrush,
    };

    protected FormattedText Text(string text, double size, Brush brush, bool strong = false)
    {
        var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, strong ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    protected Pen Pen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        return pen;
    }

    /// <summary>Faint fill used to highlight the hovered category.</summary>
    protected Brush HoverFill
    {
        get
        {
            var b = LineBrush.Clone();
            b.Opacity = 0.45;
            return b;
        }
    }

    private void DrawCentered(DrawingContext dc, string text, Rect area)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = Text(text, 13, MutedBrush);
        dc.DrawText(ft, new Point(area.Left + (area.Width - ft.Width) / 2, area.Top + (area.Height - ft.Height) / 2));
    }

    /// <summary>Draws the legend (wrapping) and returns its height.</summary>
    private double DrawLegend(DrawingContext dc, ChartData data, double width)
    {
        double x = 0, y = 0, lineH = 0;
        foreach (var s in data.Series)
        {
            var ft = Text(s.Name, LegendFontSize, MutedBrush);
            var itemW = 18 + ft.Width + 16;
            if (x > 0 && x + itemW > width)
            {
                x = 0;
                y += lineH + 4;
            }
            lineH = Math.Max(lineH, ft.Height);
            var cy = y + ft.Height / 2;
            var brush = BrushOf(s.Color);
            if (IsLine(s))
            {
                dc.DrawLine(Pen(brush, 2), new Point(x, cy), new Point(x + 12, cy));
                dc.DrawEllipse(brush, null, new Point(x + 6, cy), 3, 3);
            }
            else
            {
                dc.DrawRoundedRectangle(brush, null, new Rect(x + 1, cy - 5, 10, 10), 2, 2);
            }
            dc.DrawText(ft, new Point(x + 18, y));
            x += itemW;
        }
        return y + lineH;
    }

    /// <summary>A "nice" scale (ticks at 1/2/2.5/5 × 10ⁿ) covering [min, max]. Handles all-zero data.</summary>
    protected static (double Low, double High, double Step) NiceScale(double min, double max, bool integers, int ticks = 4)
    {
        if (double.IsNaN(min) || double.IsInfinity(min)) min = 0;
        if (double.IsNaN(max) || double.IsInfinity(max)) max = 0;
        if (max - min <= 0) max = min + (integers ? Math.Max(1, ticks) : 1);
        var raw = (max - min) / ticks;
        var exp = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var f = raw / exp;
        var step = (f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10) * exp;
        if (integers) step = Math.Max(1, Math.Ceiling(step));
        var low = Math.Floor(min / step) * step;
        var high = Math.Ceiling(max / step) * step;
        if (high <= low) high = low + step;
        return (low, high, step);
    }

    /// <summary>Lowest and highest drawn values (0 always included so bars start at the baseline).</summary>
    protected static (double Min, double Max) Extent(ChartData data)
    {
        double min = 0, max = 0;
        foreach (var s in data.Series)
            foreach (var v in s.Values)
            {
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
        if (data.Maximum is { } m) max = Math.Max(max, m);
        return (min, max);
    }

    protected static double ValueAt(ChartSeries s, int i) => i < s.Values.Count ? s.Values[i] : double.NaN;

    /// <summary>
    /// Vertical chart: value axis on the left, categories along the bottom. Bars are grouped per category,
    /// line series are drawn over them through the category centres.
    /// </summary>
    protected void RenderVertical(DrawingContext dc, ChartData data, Rect area)
    {
        var (min, max) = Extent(data);
        var (lo, hi, step) = NiceScale(min, max, data.Format != ChartValueFormat.Percent);
        var tickLabels = new List<(double V, FormattedText Ft)>();
        for (var v = lo; v <= hi + step / 2; v += step) tickLabels.Add((v, Text(data.FormatAxis(v), AxisFontSize, MutedBrush)));
        var axisW = tickLabels.Max(t => t.Ft.Width) + 10;
        var catH = Text("Ag", AxisFontSize, MutedBrush).Height + 6;
        var plot = new Rect(area.Left + axisW, area.Top + 6, Math.Max(1, area.Width - axisW), Math.Max(1, area.Height - 6 - catH));
        double Y(double v) => plot.Bottom - (v - lo) / (hi - lo) * plot.Height;

        var n = data.Categories.Count;
        var bw = plot.Width / n;
        for (var i = 0; i < n; i++) Bands.Add(new Rect(plot.Left + i * bw, area.Top, bw, area.Height));
        if (Hover >= 0 && Hover < n) dc.DrawRoundedRectangle(HoverFill, null, new Rect(plot.Left + Hover * bw + 2, plot.Top, Math.Max(0, bw - 4), plot.Height), 4, 4);

        // Gridlines and value labels.
        var grid = new Pen(LineBrush, 1);
        foreach (var (v, ft) in tickLabels)
        {
            var y = Math.Round(Y(v)) + 0.5;
            dc.DrawLine(v == 0 ? new Pen(MutedBrush, 1) : grid, new Point(plot.Left, y), new Point(plot.Right, y));
            dc.DrawText(ft, new Point(area.Left + axisW - 8 - ft.Width, y - ft.Height / 2));
        }

        // Category labels (every k-th one when they would overlap).
        var labels = data.Categories.Select(c => Text(c, AxisFontSize, MutedBrush)).ToList();
        var every = Math.Max(1, (int)Math.Ceiling((labels.Max(l => l.Width) + 8) / bw));
        for (var i = 0; i < n; i += every)
        {
            var ft = labels[i];
            if (i == Hover) ft = Text(data.Categories[i], AxisFontSize, InkBrush, strong: true);
            dc.DrawText(ft, new Point(plot.Left + i * bw + (bw - ft.Width) / 2, plot.Bottom + 5));
        }

        // Bars.
        var bars = data.Series.Where(s => !IsLine(s)).ToList();
        if (bars.Count > 0)
        {
            var groupW = Math.Min(bw * 0.7, bars.Count * 28);
            var barW = groupW / bars.Count;
            var gap = barW > 6 ? 2 : 0;
            var showValues = bars.Count == 1 && data.Series.Count == 1;
            for (var i = 0; i < n; i++)
            {
                var x0 = plot.Left + i * bw + (bw - groupW) / 2;
                for (var k = 0; k < bars.Count; k++)
                {
                    var v = ValueAt(bars[k], i);
                    if (double.IsNaN(v) || v == 0) continue;
                    var y1 = Y(Math.Max(0, v));
                    var y2 = Y(Math.Min(0, v));
                    var rect = new Rect(x0 + k * barW + gap / 2.0, y1, Math.Max(1, barW - gap), Math.Max(1, y2 - y1));
                    dc.DrawRoundedRectangle(BrushOf(bars[k].Color), null, rect, Math.Min(3, rect.Width / 2), Math.Min(3, rect.Width / 2));
                    if (showValues)
                    {
                        var ft = Text(data.FormatAxis(v), AxisFontSize - 0.5, i == Hover ? InkBrush : MutedBrush);
                        if (ft.Width <= bw - 2 && y1 - ft.Height - 2 >= area.Top)
                            dc.DrawText(ft, new Point(rect.Left + (rect.Width - ft.Width) / 2, y1 - ft.Height - 2));
                    }
                }
            }
        }

        // Lines (broken where a value is missing).
        foreach (var s in data.Series.Where(IsLine))
        {
            var brush = BrushOf(s.Color);
            var pen = Pen(brush, 2);
            Point? previous = null;
            var points = new List<Point>();
            for (var i = 0; i < n; i++)
            {
                var v = ValueAt(s, i);
                if (double.IsNaN(v))
                {
                    previous = null;
                    continue;
                }
                var p = new Point(plot.Left + i * bw + bw / 2, Y(v));
                if (previous is { } q) dc.DrawLine(pen, q, p);
                previous = p;
                points.Add(p);
            }
            foreach (var p in points) dc.DrawEllipse(PanelBrush, Pen(brush, 2), p, 3.5, 3.5);
            if (data.Series.Count == 1)
                for (var i = 0; i < n; i++)
                {
                    var v = ValueAt(s, i);
                    if (double.IsNaN(v) || (i != Hover && i != n - 1)) continue;
                    var ft = Text(data.FormatAxis(v), AxisFontSize - 0.5, InkBrush, strong: true);
                    var y = Y(v) - ft.Height - 6;
                    if (y < area.Top) y = Y(v) + 6;
                    var x = Math.Clamp(plot.Left + i * bw + (bw - ft.Width) / 2, plot.Left, plot.Right - ft.Width);
                    dc.DrawText(ft, new Point(x, y));
                }
        }
    }

    // ---------- Hover / tooltip ----------

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        var index = Bands.FindIndex(b => b.Contains(p));
        if (index == _hover) return;
        _hover = index;
        if (index >= 0 && Data is { } data)
            _tip.Content = data.Describe(index);
        else
            _tip.IsOpen = false;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ResetHover();
    }

    private void ResetHover()
    {
        if (_hover < 0) return;
        _hover = -1;
        _tip.IsOpen = false;
        InvalidateVisual();
    }
}
