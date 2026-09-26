using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CentreSoutien.Presentation.Core;

/// <summary>Colour of a status badge. Views map it to the theme's ok / warn / bad brushes.</summary>
public enum BadgeKind
{
    Neutral,
    Ok,
    Warn,
    Bad,
    Outline,
}

public sealed record Badge(string Text, BadgeKind Kind)
{
    public static Badge For(PaymentState s) => new(Labels.Of(s), s switch
    {
        PaymentState.Paid => BadgeKind.Ok,
        PaymentState.Partial => BadgeKind.Warn,
        PaymentState.Unpaid => BadgeKind.Bad,
        _ => BadgeKind.Neutral,
    });

    public static Badge For(AttendanceStatus s) => new(Labels.Of(s), s switch
    {
        AttendanceStatus.Present => BadgeKind.Ok,
        AttendanceStatus.Late => BadgeKind.Warn,
        AttendanceStatus.Absent => BadgeKind.Bad,
        _ => BadgeKind.Neutral,
    });

    public static Badge For(SessionStatus s) => new(Labels.Of(s), s switch
    {
        SessionStatus.Done => BadgeKind.Ok,
        SessionStatus.Cancelled => BadgeKind.Bad,
        _ => BadgeKind.Outline,
    });

    public static Badge Active(bool active) => active ? new("Actif", BadgeKind.Ok) : new("Inactif", BadgeKind.Neutral);
}

/// <summary>A selectable option rendered as a chip or list entry.</summary>
public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Label + value pair for info grids and summaries.</summary>
public sealed record Field(string Label, string Value);

/// <summary>Big figure tile (dashboard, payments…).</summary>
public sealed record Kpi(string Label, string Value, string? Note = null);

public static class Options
{
    public static readonly IReadOnlyList<Option<PaymentMethod>> PaymentMethods =
        Enum.GetValues<PaymentMethod>().Select(m => new Option<PaymentMethod>(m, Labels.Of(m))).ToList();

    public static readonly IReadOnlyList<Option<PaymentKind>> PaymentKinds =
        Enum.GetValues<PaymentKind>().Select(m => new Option<PaymentKind>(m, Labels.Of(m))).ToList();

    public static readonly IReadOnlyList<Option<CompensationType>> CompensationTypes =
        Enum.GetValues<CompensationType>().Select(m => new Option<CompensationType>(m, Labels.Of(m))).ToList();

    public static readonly IReadOnlyList<Option<DayOfWeek>> Days =
        Labels.WeekOrder.Select(d => new Option<DayOfWeek>(d, Labels.Day(d))).ToList();

    public static readonly IReadOnlyList<Option<SessionStatus>> SessionStatuses =
        Enum.GetValues<SessionStatus>().Select(m => new Option<SessionStatus>(m, Labels.Of(m))).ToList();

    public static readonly IReadOnlyList<Option<ExamType>> ExamTypes =
        Enum.GetValues<ExamType>().Select(m => new Option<ExamType>(m, Labels.Of(m))).ToList();

    public static readonly IReadOnlyList<Option<DiscountType>> DiscountTypes =
        Enum.GetValues<DiscountType>().Select(m => new Option<DiscountType>(m, Labels.Of(m))).ToList();

    public static readonly IReadOnlyList<Option<Gender>> Genders =
    [
        new(Gender.Unspecified, "—"), new(Gender.Male, "Garçon"), new(Gender.Female, "Fille"),
    ];

    public static readonly IReadOnlyList<string> DefaultLevels = ["1AM", "2AM", "3AM", "4AM", "1AS", "2AS", "3AS"];

    public static readonly IReadOnlyList<string> ExpenseCategories =
        ["Loyer", "Charges", "Fournitures", "Internet", "Entretien", "Salaires administratifs", "Publicité", "Équipement", "Autre"];

    public static readonly IReadOnlyList<string> DocumentCategories =
        ["Fiche d'inscription", "Certificat de scolarité", "Pièce d'identité", "Photo", "Bulletin", "Contrat", "Facture", "Autre"];

    /// <summary>The 12 months around <paramref name="current"/> (6 back, 5 ahead) for period pickers.</summary>
    public static IReadOnlyList<Option<DateTime>> Months(DateTime current)
    {
        var p = Period.Of(current);
        return Enumerable.Range(-6, 12).Select(i => p.AddMonths(i)).Select(m => new Option<DateTime>(m, Labels.Month(m))).ToList();
    }

    public static IReadOnlyList<int> LockDelays => [0, 5, 10, 15, 30, 60];
}

/// <summary>Parses user-typed amounts like "4 500", "4500,50" or "4.500".</summary>
public static class Parse
{
    public static decimal? Amount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = new string(text.Where(c => char.IsDigit(c) || c is ',' or '.' or '-').ToArray()).Replace(',', '.');
        // "4.500" typed as a thousands separator.
        if (t.Count(c => c == '.') > 1 || (t.Contains('.') && t.Length - t.IndexOf('.') == 4)) t = t.Replace(".", "");
        return decimal.TryParse(t, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public static TimeSpan? Time(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().ToLowerInvariant().Replace('h', ':');
        if (t.EndsWith(':')) t += "00";
        if (!t.Contains(':')) t += ":00";
        return TimeSpan.TryParse(t, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= TimeSpan.Zero && v < TimeSpan.FromDays(1) ? v : null;
    }

    public static string Time(TimeSpan t) => t.ToString(@"hh\:mm");
}

/// <summary>Toggle state for settings switches.</summary>
public sealed partial class Toggle(string label, bool value) : ObservableObject
{
    public string Label => label;

    [ObservableProperty]
    private bool _isOn = value;
}
