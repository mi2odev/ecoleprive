using System.Windows;
using System.Windows.Media;
using CentreSoutien.Presentation.Core;

namespace CentreSoutien.Desktop.Controls;

/// <summary>Line chart: every series is drawn as a line through the category centres (missing values break the line).</summary>
public sealed class LineChart : ChartBase
{
    protected override bool IsLine(ChartSeries series) => true;

    protected override void RenderPlot(DrawingContext dc, ChartData data, Rect area) => RenderVertical(dc, data, area);
}
