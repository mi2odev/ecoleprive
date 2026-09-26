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

    private void HookSearch()
    {
        if (_search is not null) _search.PropertyChanged -= OnSearchChanged;
        _search = Shell?.Search;
        if (_search is not null) _search.PropertyChanged += OnSearchChanged;
    }

    private void OnSearchChanged(object? sender, PropertyChangedEventArgs e)
    {
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
}
