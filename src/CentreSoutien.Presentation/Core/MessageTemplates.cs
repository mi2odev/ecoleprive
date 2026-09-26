using System.Text;
using System.Text.RegularExpressions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;

namespace CentreSoutien.Presentation.Core;

/// <summary>Values substituted into a message template. Null values become empty (except {parent}).</summary>
public sealed record MessageValues
{
    public string? Parent { get; init; }
    public string? Student { get; init; }
    public string? Month { get; init; }
    public string? Balance { get; init; }
    public string? MonthlyFee { get; init; }
    public string? Sessions { get; init; }
    public string? Course { get; init; }
    public string? Date { get; init; }
    public string? Center { get; init; }
    public string? Phone { get; init; }
}

/// <summary>
/// Builds the messages sent to parents (payment reminders, absence notices) from the templates of
/// <see cref="CenterSettings"/>, and turns local phone numbers into WhatsApp numbers. Pure functions.
/// </summary>
public static partial class MessageTemplates
{
    /// <summary>Used for {parent} when the student has no parent recorded.</summary>
    public const string NoParentGreeting = "Madame, Monsieur";

    /// <summary>Placeholders understood by <see cref="Fill"/> (shown in Settings).</summary>
    public static IReadOnlyList<(string Key, string Description)> Placeholders { get; } =
    [
        ("{parent}", "nom du parent"),
        ("{eleve}", "nom de l'élève"),
        ("{seances}", "groupes et séances (ex. Maths 3AS A · séance 3/4)"),
        ("{reste}", "reste à payer"),
        ("{prix}", "prix des prochaines séances"),
        ("{mois}", "mois depuis lequel un paiement est dû"),
        ("{cours}", "cours (absences)"),
        ("{date}", "date de la séance"),
        ("{centre}", "nom du centre"),
        ("{telephone}", "téléphone du centre"),
    ];

    public static string PlaceholderHelp { get; } = string.Join(" · ", Placeholders.Select(p => $"{p.Key} {p.Description}"));

    [GeneratedRegex(@"\{([\p{L}_]+)\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex SpacesRegex();

    [GeneratedRegex(@"[\s\-–—,:]+$")]
    private static partial Regex TrailingSeparatorsRegex();

    /// <summary>
    /// Replaces the placeholders of <paramref name="template"/> (case-insensitive, accents optional: {élève} = {eleve}).
    /// Unknown placeholders are kept as typed. Empty values leave no double spaces nor dangling separator at the end.
    /// </summary>
    public static string Fill(string? template, MessageValues values)
    {
        if (string.IsNullOrWhiteSpace(template)) return "";
        var text = PlaceholderRegex().Replace(template, m => Value(Key(m.Groups[1].Value), values) ?? m.Value);
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => SpacesRegex().Replace(l, " ").TrimEnd());
        return TrailingSeparatorsRegex().Replace(string.Join("\n", lines), "").Trim();
    }

    private static string Key(string raw)
    {
        var normalized = raw.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }

    private static string? Value(string key, MessageValues v) => key switch
    {
        "parent" => string.IsNullOrWhiteSpace(v.Parent) ? NoParentGreeting : v.Parent.Trim(),
        "eleve" => v.Student ?? "",
        "mois" => v.Month ?? "",
        "reste" => v.Balance ?? "",
        "mensualite" or "prix" => v.MonthlyFee ?? "",
        "seances" => v.Sessions ?? "",
        "cours" => v.Course ?? "",
        "date" => v.Date ?? "",
        "centre" => v.Center ?? "",
        "telephone" => v.Phone ?? "",
        _ => null,
    };

    /// <summary>"septembre 2026".</summary>
    public static string MonthText(DateTime month) => Labels.Month(month).ToLowerInvariant();

    /// <summary>"samedi 26 septembre 2026".</summary>
    public static string DateText(DateTime date) => Labels.LongDate(date).ToLowerInvariant();

    /// <summary>Payment reminder built from <see cref="CenterSettings.PaymentReminderTemplate"/> (or the default one if empty).</summary>
    /// <param name="since">Start of the oldest pack of sessions not paid.</param>
    /// <param name="sessions">"Maths 3AS A · séance 3/4".</param>
    /// <param name="packPrice">Price of the next pack of sessions.</param>
    public static string PaymentReminder(CenterSettings s, string? parent, string student, DateTime since, string sessions, decimal packPrice, decimal balance) =>
        Fill(Or(s.PaymentReminderTemplate, CenterSettings.DefaultPaymentReminderTemplate), new MessageValues
        {
            Parent = parent, Student = student, Month = MonthText(since), Balance = Money.Format(balance), MonthlyFee = Money.Format(packPrice),
            Sessions = sessions, Center = s.CenterName, Phone = s.Phone,
        });

    /// <summary>Short reminder for an SMS (fits in one or two SMS, no greeting).</summary>
    public static string ShortPaymentReminder(CenterSettings s, string student, decimal balance)
    {
        var text = $"{s.CenterName} : séances de {student} non réglées, reste {Money.Format(balance)}. Merci.";
        return string.IsNullOrWhiteSpace(s.Phone) ? text : $"{text} {s.Phone.Trim()}";
    }

    /// <summary>Absence notice built from <see cref="CenterSettings.AbsenceMessageTemplate"/> (or the default one if empty).</summary>
    public static string AbsenceNotice(CenterSettings s, string? parent, string student, string course, DateTime date) =>
        Fill(Or(s.AbsenceMessageTemplate, CenterSettings.DefaultAbsenceMessageTemplate), new MessageValues
        {
            Parent = parent, Student = student, Course = course, Date = DateText(date), Center = s.CenterName, Phone = s.Phone,
        });

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    /// <summary>
    /// International number in digits only, as WhatsApp expects ("0661 24 18 90" → "213661241890").
    /// Accepts "+213…", "00213…", "213…", "0…" or a number without its leading 0; only the first number of
    /// "0661… / 0555…" is used. Returns null when there is no usable number.
    /// </summary>
    public static string? NormalizePhone(string? phone, string? countryCode = CenterSettings.DefaultPhoneCountryCode)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var cc = Digits(countryCode);
        if (cc.Length == 0) cc = CenterSettings.DefaultPhoneCountryCode;

        var first = phone.Split(['/', ',', ';', '|', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()).FirstOrDefault(p => Digits(p).Length > 0);
        if (first is null) return null;
        var digits = Digits(first);

        string international;
        if (first.StartsWith('+')) international = digits;
        else if (digits.StartsWith("00")) international = digits[2..];
        else if (digits.StartsWith('0')) international = cc + digits[1..];
        else if (digits.StartsWith(cc) && digits.Length >= cc.Length + 8) international = digits;
        else international = cc + digits;

        // "+213 (0)661…": drop the national 0 kept after the country code.
        if (international.StartsWith(cc + "0")) international = cc + international[(cc.Length + 1)..];

        var national = international.StartsWith(cc) ? international.Length - cc.Length : international.Length;
        return national >= 8 && international.Length <= 15 ? international : null;
    }

    /// <summary>wa.me link opening a chat with the message typed in; null when the phone number is unusable.</summary>
    public static string? WhatsAppUrl(string? phone, string? countryCode, string text) =>
        NormalizePhone(phone, countryCode) is { } number ? $"https://wa.me/{number}?text={Uri.EscapeDataString(text)}" : null;

    private static string Digits(string? s) => s is null ? "" : new string(s.Where(char.IsAsciiDigit).ToArray());
}
