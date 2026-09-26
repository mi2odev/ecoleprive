using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace CentreSoutien.Desktop.Controls;

/// <summary>
/// Opens the button's <see cref="FrameworkElement.ContextMenu"/> below it on a normal click ("Plus…" menus).
/// The menu shares the button's DataContext, so its items bind to the page's commands.
/// </summary>
public static class DropDown
{
    public static readonly DependencyProperty OpenOnClickProperty = DependencyProperty.RegisterAttached(
        "OpenOnClick", typeof(bool), typeof(DropDown), new PropertyMetadata(false, OnChanged));

    public static bool GetOpenOnClick(DependencyObject d) => (bool)d.GetValue(OpenOnClickProperty);
    public static void SetOpenOnClick(DependencyObject d, bool value) => d.SetValue(OpenOnClickProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button) return;
        button.Click -= OnClick;
        if (e.NewValue is true) button.Click += OnClick;
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase button || button.ContextMenu is not { } menu) return;
        if (menu.IsOpen)
        {
            menu.IsOpen = false;
            return;
        }
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.VerticalOffset = 4;
        menu.MinWidth = Math.Max(menu.MinWidth, button.ActualWidth);
        if (BindingOperations.GetBinding(menu, FrameworkElement.DataContextProperty) is null)
            menu.SetBinding(FrameworkElement.DataContextProperty, new Binding(nameof(FrameworkElement.DataContext)) { Source = button });
        menu.IsOpen = true;
        e.Handled = true;
    }
}

/// <summary>
/// Friendly placeholder for an empty list: glyph, title, explanation and call-to-action buttons (the content).
/// </summary>
public class EmptyState : ContentControl
{
    // Look: implicit style in Themes/Controls.xaml.
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState));
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(nameof(Message), typeof(string), typeof(EmptyState));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(EmptyState),
        new PropertyMetadata(""));

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Message { get => (string?)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    /// <summary>Segoe Fluent Icons / MDL2 glyph shown above the title.</summary>
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
}

/// <summary>
/// Keyboard marking of list rows (attendance): when the row (or a control inside it) has focus, a letter key runs
/// <see cref="CommandProperty"/> with the letter as parameter ("P", "A", "R", "E"…) and focus moves to the next row.
/// Up / Down arrows move between rows. The command's CanExecute decides which letters are accepted.
/// </summary>
public static class RowKeys
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(RowKeys), new PropertyMetadata(null, OnChanged));

    public static ICommand? GetCommand(DependencyObject d) => (ICommand?)d.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject d, ICommand? value) => d.SetValue(CommandProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        el.KeyDown -= OnKey;
        el.MouseLeftButtonDown -= OnMouseDown;
        if (e.NewValue is null) return;
        el.KeyDown += OnKey;
        el.MouseLeftButtonDown += OnMouseDown;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement el && !el.IsKeyboardFocusWithin) el.Focus();
    }

    private static void OnKey(object sender, KeyEventArgs e)
    {
        if (sender is not UIElement row || e.Handled || Keyboard.Modifiers != ModifierKeys.None) return;
        // Never steal letters typed in a text field inside the row.
        if (e.OriginalSource is TextBoxBase) return;
        switch (e.Key)
        {
            case Key.Down:
                e.Handled = MoveFocus(row, +1);
                return;
            case Key.Up:
                e.Handled = MoveFocus(row, -1);
                return;
        }
        if (e.Key < Key.A || e.Key > Key.Z) return;
        var letter = e.Key.ToString();
        var cmd = GetCommand(row);
        if (cmd?.CanExecute(letter) != true) return;
        cmd.Execute(letter);
        e.Handled = true;
        MoveFocus(row, +1);
    }

    /// <summary>Focuses the row <paramref name="offset"/> positions away in the same list. Returns false at the ends.</summary>
    public static bool MoveFocus(UIElement row, int offset)
    {
        var list = FindAncestor<ItemsControl>(row);
        if (list?.ContainerFromElement(row) is not { } container) return false;
        var index = list.ItemContainerGenerator.IndexFromContainer(container);
        if (index < 0) return false;
        var target = index + offset;
        if (target < 0 || target >= list.Items.Count) return false;
        if (list.ItemContainerGenerator.ContainerFromIndex(target) is not DependencyObject next) return false;
        if (FindRow(next) is not { } nextRow) return false;
        nextRow.Focus();
        if (nextRow is FrameworkElement fe) fe.BringIntoView();
        return true;
    }

    private static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        var p = VisualTreeHelper.GetParent(d);
        while (p is not null and not T) p = VisualTreeHelper.GetParent(p);
        return p as T;
    }

    private static UIElement? FindRow(DependencyObject d)
    {
        if (d is UIElement el && GetCommand(el) is not null) return el;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (FindRow(VisualTreeHelper.GetChild(d, i)) is { } found) return found;
        return null;
    }
}
