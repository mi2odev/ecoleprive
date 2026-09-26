using System.Globalization;
using CentreSoutien.Domain.Calculations;

namespace CentreSoutien.Presentation.Core;

/// <summary>Theme colour of a chart series. Views map it to the theme brushes (accent, ok, warn, bad, muted, ink).</summary>
public enum ChartColor
{
    Accent,
    Ok,
    Warn,
    Bad,
    Muted,
    Ink,
}

/// <summary>How a series is drawn by a bar chart (line charts draw every series as a line).</summary>
public enum ChartSeriesKind
{
    Bar,
    Line,
}

public enum ChartValueFormat
{
    /// <summary>Amount: "45 000" on the axis, "45 000 DZD" in tooltips.</summary>
    Money,
    /// <summary>Percentage (0–100).</summary>
    Percent,
    /// <summary>Plain count.</summary>
    Count,
}

/// <summary>One series of values, one per category. <see cref="double.NaN"/> means "no value" (nothing drawn).</summary>
public sealed record ChartSeries(string Name, IReadOnlyList<double> Values, ChartColor Color, ChartSeriesKind Kind = ChartSeriesKind.Bar);

/// <summary>Everything a chart control needs: category labels, series, value format and an optional fixed maximum (e.g. 100 for percentages).</summary>
public sealed record ChartData(IReadOnlyList<string> Categories, IReadOnlyList<ChartSeries> Series, ChartValueFormat Format = ChartValueFormat.Count, double? Maximum = null)
{
    public static readonly ChartData Empty = new([], []);

    /// <summary>True when there is nothing but zeros / missing values to draw.</summary>
    public bool IsEmpty => Categories.Count == 0 || Series.All(s => s.Values.All(v => double.IsNaN(v) || v == 0));

    /// <summary>Short value for axis ticks and bar labels.</summary>
    public string FormatAxis(double v) => Format switch
    {
        ChartValueFormat.Money => Money.Number((decimal)v),
        ChartValueFormat.Percent => v.ToString("0", CultureInfo.InvariantCulture) + " %",
        _ => v.ToString("0.#", CultureInfo.InvariantCulture),
    };

    /// <summary>Full value for tooltips.</summary>
    public string FormatValue(double v) => double.IsNaN(v) ? "—" : Format switch
    {
        ChartValueFormat.Money => Money.Format((decimal)v),
        ChartValueFormat.Percent => v.ToString("0.#", CultureInfo.InvariantCulture) + " %",
        _ => v.ToString("0.#", CultureInfo.InvariantCulture),
    };

    /// <summary>Tooltip text for one category: its label and every series value.</summary>
    public string Describe(int category)
    {
        if (category < 0 || category >= Categories.Count) return "";
        var lines = Series.Select(s => $"{s.Name} : {FormatValue(category < s.Values.Count ? s.Values[category] : double.NaN)}");
        return string.Join(Environment.NewLine, new[] { Categories[category] }.Concat(lines));
    }
}

public static class ChartLabels
{
    public static readonly string[] ShortMonths =
        ["Janv.", "Févr.", "Mars", "Avr.", "Mai", "Juin", "Juil.", "Août", "Sept.", "Oct.", "Nov.", "Déc."];

    public static string Month(DateTime period) => ShortMonths[period.Month - 1];

    /// <summary>Week label from its first day, e.g. "12/09".</summary>
    public static string Week(DateTime start) => $"{start.Day:00}/{start.Month:00}";
}
