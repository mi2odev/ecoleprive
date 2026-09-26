using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CentreSoutien.Desktop.Controls;

/// <summary>Two-way bindable password for <see cref="PasswordBox"/> (which deliberately has no bindable Password).</summary>
public static class PasswordBinding
{
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password", typeof(string), typeof(PasswordBinding),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating", typeof(bool), typeof(PasswordBinding));

    public static string GetPassword(DependencyObject d) => (string)d.GetValue(PasswordProperty);
    public static void SetPassword(DependencyObject d, string value) => d.SetValue(PasswordProperty, value);

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        box.PasswordChanged -= OnBoxChanged;
        if (!(bool)box.GetValue(IsUpdatingProperty) && box.Password != (string?)e.NewValue)
            box.Password = (string?)e.NewValue ?? "";
        box.PasswordChanged += OnBoxChanged;
    }

    private static void OnBoxChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        box.SetValue(IsUpdatingProperty, true);
        SetPassword(box, box.Password);
        box.SetValue(IsUpdatingProperty, false);
    }
}

/// <summary>Moves keyboard focus to the element when it becomes visible (login, lock screen, dialogs).</summary>
public static class FocusOnShow
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(FocusOnShow), new PropertyMetadata(false, OnChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el || e.NewValue is not true) return;
        el.IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true) el.Dispatcher.BeginInvoke(() => Keyboard.Focus(el), System.Windows.Threading.DispatcherPriority.Input);
        };
        if (el is FrameworkElement fe)
            fe.Loaded += (_, _) => { if (el.IsVisible) Keyboard.Focus(el); };
    }
}

/// <summary>Runs a command when Enter is pressed in the element (search boxes, login fields).</summary>
public static class Enter
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(Enter), new PropertyMetadata(null, OnChanged));

    public static ICommand? GetCommand(DependencyObject d) => (ICommand?)d.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject d, ICommand? value) => d.SetValue(CommandProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        el.KeyDown -= OnKey;
        if (e.NewValue is not null) el.KeyDown += OnKey;
    }

    private static void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not DependencyObject d) return;
        // Push pending text to the view model before running the command.
        if (sender is TextBox tb) tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        var cmd = GetCommand(d);
        if (cmd?.CanExecute(null) == true)
        {
            cmd.Execute(null);
            e.Handled = true;
        }
    }
}
