namespace CentreSoutien.Presentation.Core;

/// <summary>Amounts written in French words for receipts ("quatre mille cinq cents dinars algériens").</summary>
public static class NumberWords
{
    private static readonly string[] Units =
    [
        "zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix",
        "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf",
    ];

    private static readonly string[] Tens = ["", "", "vingt", "trente", "quarante", "cinquante", "soixante", "soixante", "quatre-vingt", "quatre-vingt"];

    /// <summary>Whole number in French words (rectified spelling rules kept simple: "vingt et un", "quatre-vingts", "deux cents").</summary>
    public static string French(long n)
    {
        if (n < 0) return "moins " + French(-n);
        if (n < 1000) return Below1000(n, final: true);
        var parts = new List<string>();
        foreach (var (value, single, plural) in new[] { (1_000_000_000L, "un milliard", "milliards"), (1_000_000L, "un million", "millions") })
        {
            if (n < value) continue;
            var count = n / value;
            parts.Add(count == 1 ? single : $"{Below1000(count, final: true)} {plural}");
            n %= value;
        }
        if (n >= 1000)
        {
            var thousands = n / 1000;
            // "mille" never takes an s, and "cent"/"vingt" before "mille" stay singular.
            parts.Add(thousands == 1 ? "mille" : $"{Below1000(thousands, final: false)} mille");
            n %= 1000;
        }
        if (n > 0) parts.Add(Below1000(n, final: true));
        return string.Join(" ", parts);
    }

    /// <summary>"4 500" → "Quatre mille cinq cents dinars algériens".</summary>
    public static string Amount(decimal amount, string currency = "DZD")
    {
        var whole = (long)Math.Floor(Math.Abs(amount));
        var cents = (int)Math.Round((Math.Abs(amount) - whole) * 100);
        var unit = currency.Trim().ToUpperInvariant() is "DZD" or "DA" ? (whole > 1 ? "dinars algériens" : "dinar algérien") : currency;
        var text = $"{French(whole)} {unit}" + (cents > 0 ? $" et {French(cents)} centime{(cents > 1 ? "s" : "")}" : "");
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <param name="final">True when nothing follows (then "cents" and "quatre-vingts" take their s).</param>
    private static string Below1000(long n, bool final)
    {
        var hundreds = n / 100;
        var rest = n % 100;
        var parts = new List<string>();
        if (hundreds > 0)
            parts.Add(hundreds == 1 ? "cent" : $"{Units[hundreds]} cent{(rest == 0 && final ? "s" : "")}");
        if (rest > 0 || hundreds == 0) parts.Add(Below100(rest, final));
        return string.Join(" ", parts);
    }

    private static string Below100(long n, bool final)
    {
        if (n < 20) return Units[n];
        var ten = n / 10;
        var unit = n % 10;
        if (ten is 7 or 9) // soixante-dix…, quatre-vingt-dix…
            return Tens[ten] + (ten == 7 && unit == 1 ? " et " : "-") + Units[10 + unit];
        if (unit == 0) return Tens[ten] + (ten == 8 && final ? "s" : "");
        return Tens[ten] + (unit == 1 && ten != 8 ? " et un" : "-" + Units[unit]);
    }
}
