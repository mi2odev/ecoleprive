using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CentreSoutien.Presentation.Core;

// Platform services the view models need. The WPF project implements them;
// tests use simple fakes.

public interface INavigator
{
    Task NavigateAsync<TPage>(object? parameter = null) where TPage : PageViewModel;
    Task NavigateAsync(Type pageType, object? parameter = null);
    bool CanGoBack { get; }
    Task BackAsync();
}

public interface INotifier
{
    void Info(string message);
    void Error(string message);
}

public interface IFilePicker
{
    string? OpenFile(string title, string filter);
    string? SaveFile(string title, string defaultName, string filter);
    string? PickFolder(string title);
}

public interface IShell
{
    /// <summary>Opens a file with its default application (documents, exports).</summary>
    void Open(string path);
    /// <summary>Shows a file in Explorer.</summary>
    void Reveal(string path);
}

public interface IThemeService
{
    AppTheme Current { get; }
    void Apply(AppTheme theme);
}

public sealed record PrintTable(string Title, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<int>? RightAligned = null);

public interface IPrintService
{
    void PrintReceipt(StudentPayment payment, CenterSettings settings, string? logoPath);
    /// <summary>Prints (or saves as PDF through the "Microsoft Print to PDF" printer) a simple report.</summary>
    void PrintReport(string title, string subtitle, CenterSettings settings, IReadOnlyList<(string Label, string Value)> summary, IReadOnlyList<PrintTable> tables);
}

/// <summary>Toast message shown at the bottom of the window.</summary>
public sealed partial class Notifier : ObservableObject, INotifier
{
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isError;

    public void Info(string message) => Show(message, false);
    public void Error(string message) => Show(message, true);

    private async void Show(string message, bool error)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Message = message;
        IsError = error;
        try
        {
            await Task.Delay(error ? 5000 : 2600, cts.Token);
            Message = null;
        }
        catch (TaskCanceledException)
        {
        }
    }
}
