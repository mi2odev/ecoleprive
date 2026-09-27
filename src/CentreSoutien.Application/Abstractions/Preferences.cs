namespace CentreSoutien.Application.Abstractions;

/// <summary>
/// Small per-computer UI preferences (text size, dismissed hints…) kept outside the database,
/// so changing them needs no migration and they are not part of backups.
/// </summary>
public interface IUserPreferences
{
    T Get<T>(string key, T defaultValue);
    void Set<T>(string key, T value);
}

public static class PreferenceKeys
{
    /// <summary>UI scale factor: 1.0 (Normal), 1.15 (Grand), 1.3 (Très grand).</summary>
    public const string TextScale = "ui.textScale";
    /// <summary>True once the owner hid the "Premiers pas" checklist.</summary>
    public const string OnboardingDismissed = "ui.onboardingDismissed";
    /// <summary>True once the owner hid the page help panels by default.</summary>
    public const string HelpCollapsed = "ui.helpCollapsed";
    /// <summary>Order of the attendance sheet: "Nom", "Prénom", "Matricule" or "À saisir".</summary>
    public const string AttendanceSort = "ui.attendanceSort";
}
