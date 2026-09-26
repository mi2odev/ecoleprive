using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;

namespace CentreSoutien.Desktop.Views;

/// <summary>
/// Convention: a view model named <c>FooViewModel</c> is displayed by the view <c>FooView</c>
/// (any namespace under CentreSoutien.Desktop.Views). Pages and dialogs are resolved the same way.
/// </summary>
public sealed class ViewLocator : DataTemplateSelector
{
    private static readonly ConcurrentDictionary<Type, DataTemplate> Cache = new();
    private static readonly Dictionary<string, Type> Views = typeof(ViewLocator).Assembly.GetTypes()
        .Where(t => typeof(FrameworkElement).IsAssignableFrom(t) && t.Name.EndsWith("View") && t.Namespace?.StartsWith("CentreSoutien.Desktop.Views") == true)
        .ToDictionary(t => t.Name);

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is null) return null;
        return Cache.GetOrAdd(item.GetType(), vmType =>
        {
            var name = vmType.Name.EndsWith("ViewModel") ? vmType.Name[..^"Model".Length] : vmType.Name + "View";
            var viewType = Views.GetValueOrDefault(name) ?? typeof(MissingView);
            return new DataTemplate(vmType) { VisualTree = new FrameworkElementFactory(viewType) };
        });
    }
}

/// <summary>Shown if a view model has no matching view (should not happen in a release build).</summary>
public sealed class MissingView : TextBlock
{
    public MissingView()
    {
        Margin = new Thickness(24);
        SetResourceReference(ForegroundProperty, "MutedBrush");
        DataContextChanged += (_, e) => Text = "Écran indisponible : " + e.NewValue?.GetType().Name;
    }
}
