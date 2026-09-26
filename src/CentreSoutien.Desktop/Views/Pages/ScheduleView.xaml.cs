using System.Windows;
using System.Windows.Input;

namespace CentreSoutien.Desktop.Views.Pages;

public partial class ScheduleView
{
    public ScheduleView() => InitializeComponent();

    /// <summary>The grid's horizontal scroller would swallow the mouse wheel: hand it to the page scroller instead.</summary>
    private void OnGridMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not UIElement element) return;
        e.Handled = true;
        var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = sender };
        (System.Windows.Media.VisualTreeHelper.GetParent(element) as UIElement)?.RaiseEvent(forwarded);
    }
}
