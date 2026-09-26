using CentreSoutien.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CentreSoutien.Presentation.Core;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _error;

    public bool HasError => !string.IsNullOrEmpty(Error);

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));

    /// <summary>
    /// Runs an operation with busy state. Business errors are shown inline (<see cref="Error"/>) and,
    /// when a notifier is available, as a toast; unexpected errors are reported the same way.
    /// Returns false when the operation failed.
    /// </summary>
    protected async Task<bool> RunAsync(Func<Task> action, INotifier? notifier = null)
    {
        IsBusy = true;
        Error = null;
        try
        {
            await action();
            return true;
        }
        catch (BusinessException ex)
        {
            Error = ex.Message;
            notifier?.Error(ex.Message);
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Error = "Une erreur inattendue est survenue : " + ex.Message;
            notifier?.Error(Error);
            ErrorLog.Write(ex);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>A screen reachable from the sidebar or from another screen.</summary>
public abstract partial class PageViewModel : ViewModelBase
{
    /// <summary>Navigation key of the sidebar entry to highlight (e.g. "students" for a student profile).</summary>
    public abstract string NavKey { get; }

    public abstract string Title { get; }

    /// <summary>Called after navigation. <paramref name="parameter"/> is what the caller passed (usually an id).</summary>
    public virtual Task LoadAsync(object? parameter) => Task.CompletedTask;

    /// <summary>Reloads the page with its last parameter (after an edit, a restore…).</summary>
    public virtual Task RefreshAsync() => LoadAsync(LastParameter);

    public object? LastParameter { get; set; }
}

/// <summary>Minimal file logger for unexpected errors; the path is set by the host.</summary>
public static class ErrorLog
{
    public static string? Folder { get; set; }

    public static void Write(Exception ex)
    {
        try
        {
            if (Folder is null) return;
            File.AppendAllText(Path.Combine(Folder, $"erreurs-{DateTime.Now:yyyy-MM}.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
