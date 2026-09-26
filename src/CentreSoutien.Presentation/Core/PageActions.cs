using CommunityToolkit.Mvvm.Input;

namespace CentreSoutien.Presentation.Core;

/// <summary>
/// Optional: a page whose main action ("Ajouter un élève", "Nouvelle séance"…) can be started with Ctrl+N from the shell.
/// </summary>
public interface IHasPrimaryAction
{
    IAsyncRelayCommand? PrimaryCommand { get; }
    /// <summary>Button text of the action, shown in the shortcut help.</summary>
    string? PrimaryLabel { get; }
}

/// <summary>Optional: a page that can be printed with Ctrl+P from the shell.</summary>
public interface IHasPrintAction
{
    IAsyncRelayCommand? PrintCommand { get; }
}
