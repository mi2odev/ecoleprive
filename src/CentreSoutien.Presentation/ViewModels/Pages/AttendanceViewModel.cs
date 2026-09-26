using CentreSoutien.Presentation.Core;

namespace CentreSoutien.Presentation.ViewModels.Pages;

// TODO: implement (placeholder so navigation compiles).
public sealed partial class AttendanceViewModel : PageViewModel
{
    /// <summary>Navigation parameter: open the attendance sheet of a group on a date (or a specific session).</summary>
    public sealed record Target(DateTime Date, int? GroupId = null, int? SessionId = null);

    public override string NavKey => "attendance";
    public override string Title => "Présences";
}
