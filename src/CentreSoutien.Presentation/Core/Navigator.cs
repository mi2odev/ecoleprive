using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Presentation.Core;

/// <summary>Creates page view models from the container and keeps a back stack.</summary>
public sealed partial class Navigator(IServiceProvider services) : ObservableObject, INavigator
{
    private readonly Stack<(Type Type, object? Parameter)> _history = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    private PageViewModel? _current;

    public bool CanGoBack => _history.Count > 0;

    public event EventHandler? Navigated;

    public Task NavigateAsync<TPage>(object? parameter = null) where TPage : PageViewModel => NavigateAsync(typeof(TPage), parameter);

    public Task NavigateAsync(Type pageType, object? parameter = null) => Go(pageType, parameter, push: true);

    public async Task BackAsync()
    {
        if (_history.Count == 0) return;
        var (type, parameter) = _history.Pop();
        await Go(type, parameter, push: false);
        OnPropertyChanged(nameof(CanGoBack));
    }

    /// <summary>Clears the history (sign-out) and optionally the current page.</summary>
    public void Reset()
    {
        _history.Clear();
        Current = null;
    }

    private async Task Go(Type type, object? parameter, bool push)
    {
        if (push && Current is not null) _history.Push((Current.GetType(), Current.LastParameter));
        if (_history.Count > 30) TrimHistory();
        var page = (PageViewModel)services.GetRequiredService(type);
        page.LastParameter = parameter;
        Current = page;
        Navigated?.Invoke(this, EventArgs.Empty);
        await page.LoadAsync(parameter);
    }

    private void TrimHistory()
    {
        var keep = _history.Take(20).Reverse().ToList();
        _history.Clear();
        foreach (var h in keep) _history.Push(h);
    }
}
