using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Shell;

namespace CentreSoutien.Desktop;

public partial class MainWindow : Window
{
    private SearchViewModel? _search;

    public MainWindow(Navigator navigator)
    {
        InitializeComponent();
        // Each new page starts at the top.
        navigator.Navigated += (_, _) => PageScroller.ScrollToTop();
        DataContextChanged += (_, _) => HookSearch();
        // The palette may open with text already typed in the header box: put the caret after it.
        SearchBox.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                Dispatcher.BeginInvoke(() => SearchBox.CaretIndex = SearchBox.Text.Length, DispatcherPriority.Input);
        };
    }

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    private ShellViewModel? _hookedShell;

    private void HookSearch()
    {
        if (_hookedShell is not null) _hookedShell.PropertyChanged -= OnShellChanged;
        _hookedShell = Shell;
        if (_hookedShell is not null) _hookedShell.PropertyChanged += OnShellChanged;
        if (_search is not null) _search.PropertyChanged -= OnSearchChanged;
        _search = Shell?.Search;
        if (_search is not null) _search.PropertyChanged += OnSearchChanged;
    }

    private IInputElement? _focusBeforeSearch;

    private void OnSearchChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchViewModel.IsOpen))
        {
            if (_search?.IsOpen == true) _focusBeforeSearch = Keyboard.FocusedElement;
            else
                // Give keyboard focus back (the search box is now hidden), so Tab and shortcuts keep working.
                Dispatcher.BeginInvoke(() =>
                {
                    if (_focusBeforeSearch is UIElement { IsVisible: true, IsEnabled: true } previous && !ReferenceEquals(previous, SearchBox)) Keyboard.Focus(previous);
                    else PageScroller.Focus();
                }, DispatcherPriority.Input);
        }
        if (e.PropertyName == nameof(SearchViewModel.Selected))
            Dispatcher.BeginInvoke(new Action(BringSelectedIntoView), DispatcherPriority.Loaded);
    }

    /// <summary>Keeps the result chosen with the arrow keys visible in the scrolling list.</summary>
    private void BringSelectedIntoView()
    {
        if (_search?.Selected is not { } item) return;
        FindByDataContext(SearchScroller, item)?.BringIntoView();
    }

    private static FrameworkElement? FindByDataContext(DependencyObject root, object item)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.Button { DataContext: var dc } button && ReferenceEquals(dc, item)) return button;
            if (FindByDataContext(child, item) is { } found) return found;
        }
        return null;
    }

    /// <summary>A click in the header search box opens the palette (with what was typed so far).</summary>
    private void HeaderSearch_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Shell is not { } shell) return;
        var text = shell.SearchText;
        shell.SearchText = "";
        shell.OpenSearchCommand.Execute(text);
        e.Handled = true;
    }

    /// <summary>A click outside the palette closes it.</summary>
    private void SearchOverlay_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SearchOverlay)) _search?.Close();
    }

    /// <summary>A click outside the shortcuts panel (F1) closes it.</summary>
    private void ShortcutsOverlay_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, ShortcutsOverlay) && Shell is { } shell) shell.IsShortcutsOpen = false;
    }

    private IInputElement? _focusBeforeShortcuts;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsShortcutsOpen) && Shell is { IsShortcutsOpen: true })
            _focusBeforeShortcuts = Keyboard.FocusedElement;
        // The shortcuts panel had the focus: give it back to the page so Tab and shortcuts keep working.
        if (e.PropertyName == nameof(ShellViewModel.IsShortcutsOpen) && Shell is { IsShortcutsOpen: false })
            Dispatcher.BeginInvoke(() =>
            {
                // Back to where the owner was (e.g. the current attendance row), else to the page.
                if (_focusBeforeShortcuts is UIElement { IsVisible: true, IsEnabled: true } previous) Keyboard.Focus(previous);
                else if (Keyboard.FocusedElement is not UIElement { IsVisible: true, IsEnabled: true }) PageScroller.Focus();
            }, DispatcherPriority.Input);
    }
}
