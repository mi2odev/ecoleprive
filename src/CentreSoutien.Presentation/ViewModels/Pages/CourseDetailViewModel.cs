using CentreSoutien.Presentation.Core;

namespace CentreSoutien.Presentation.ViewModels.Pages;

// TODO: implement (placeholder so navigation compiles).
public sealed partial class CourseDetailViewModel : PageViewModel
{
    /// <summary>Navigation parameter: course id and optionally the group to select.</summary>
    public sealed record Target(int CourseId, int? GroupId = null);

    public override string NavKey => "courses";
    public override string Title => "Cours";
}
