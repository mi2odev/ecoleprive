namespace CentreSoutien.Application.Abstractions;

/// <summary>"Premiers pas": what is still missing to get the center up and running, computed from the real data.</summary>
public interface IOnboardingService
{
    Task<OnboardingStatus> GetAsync(CancellationToken ct = default);
}

public enum OnboardingStep
{
    CenterInfo,
    Logo,
    Password,
    Subjects,
    Rooms,
    Teachers,
    CoursesAndGroups,
    Students,
    Backup,
}

/// <summary>One step of the checklist. Optional steps count in the progress but never keep the checklist visible.</summary>
public sealed record OnboardingItem(OnboardingStep Step, string Title, string Hint, bool IsDone, bool IsOptional = false);

public sealed record OnboardingStatus(IReadOnlyList<OnboardingItem> Steps, bool IsDatabaseEmpty)
{
    public int DoneCount => Steps.Count(s => s.IsDone);
    public int Total => Steps.Count;
    /// <summary>True when every required step is done.</summary>
    public bool IsComplete => Steps.All(s => s.IsDone || s.IsOptional);
}
