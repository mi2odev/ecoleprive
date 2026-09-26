using System.Globalization;

namespace CentreSoutien.Domain.Calculations;

public static class Money
{
    /// <summary>Currency code shown after amounts; updated from settings at startup.</summary>
    public static string Currency { get; set; } = "DZD";

    /// <summary>Formats as "4 500 DZD" (space thousands separator, no decimals).</summary>
    public static string Format(decimal amount) => Number(amount) + " " + Currency;

    public static string Number(decimal amount) =>
        Math.Round(amount, 0).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');
}
