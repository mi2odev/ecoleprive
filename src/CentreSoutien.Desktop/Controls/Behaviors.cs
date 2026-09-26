using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CentreSoutien.Presentation.Core;

namespace CentreSoutien.Desktop.Controls;

/// <summary>Two-way bindable password for <see cref="PasswordBox"/> (which deliberately has no bindable Password).</summary>
public static class PasswordBinding
{
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password", typeof(string), typeof(PasswordBinding),
        // Default is null (not ""): the change callback that hooks PasswordChanged must fire even when the
        // bound view-model value is the empty string, otherwise typed passwords never reach the view model.
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating", typeof(bool), typeof(PasswordBinding));

    public static string? GetPassword(DependencyObject d) => (string?)d.GetValue(PasswordProperty);
    public static void SetPassword(DependencyObject d, string value) => d.SetValue(PasswordProperty, value);

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        box.PasswordChanged -= OnBoxChanged;
        var value = (string?)e.NewValue ?? "";
        if (!(bool)box.GetValue(IsUpdatingProperty) && box.Password != value)
            box.Password = value;
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

    private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached(
        "Hooked", typeof(bool), typeof(FocusOnShow));

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Hook once; the handlers check the current value, so the property can be data-bound.
        if (d is not UIElement el || e.NewValue is not true || (bool)el.GetValue(HookedProperty)) return;
        el.SetValue(HookedProperty, true);
        el.IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true && GetEnabled(el)) el.Dispatcher.BeginInvoke(() => Keyboard.Focus(el), System.Windows.Threading.DispatcherPriority.Input);
        };
        if (el is FrameworkElement fe)
            fe.Loaded += (_, _) => { if (el.IsVisible && GetEnabled(el)) Keyboard.Focus(el); };
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

/// <summary>
/// Hour field ("09:00"): clicking or tabbing in selects the whole time so typing replaces it, the ":" is added
/// automatically after the hour, and the value is tidied to hh:mm when leaving the field ("9h" → "09:00").
/// </summary>
public static class TimeInput
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(TimeInput), new PropertyMetadata(false, OnChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        box.GotKeyboardFocus -= OnFocus;
        box.PreviewMouseLeftButtonDown -= OnMouseDown;
        box.PreviewTextInput -= OnTextInput;
        box.LostKeyboardFocus -= OnLeave;
        DataObject.RemovePastingHandler(box, OnPaste);
        if (e.NewValue is not true) return;
        box.MaxLength = 5;
        box.GotKeyboardFocus += OnFocus;
        box.PreviewMouseLeftButtonDown += OnMouseDown;
        box.PreviewTextInput += OnTextInput;
        box.LostKeyboardFocus += OnLeave;
        DataObject.AddPastingHandler(box, OnPaste);
    }

    private static void OnFocus(object sender, KeyboardFocusChangedEventArgs e) => ((TextBox)sender).SelectAll();

    // A click would otherwise place the caret (and undo the SelectAll): the first click only focuses and selects.
    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var box = (TextBox)sender;
        if (box.IsKeyboardFocusWithin) return;
        e.Handled = true;
        box.Focus();
    }

    private static void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = true;
        Type((TextBox)sender, e.Text);
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        e.CancelCommand();
        if (e.DataObject.GetData(DataFormats.UnicodeText) is string text) Type((TextBox)sender, text);
    }

    private static void Type(TextBox box, string input)
    {
        if (!input.Any(char.IsAsciiDigit)) return;
        box.Text = Parse.TimeMask(box.Text, box.SelectionStart, box.SelectionLength, input);
        box.CaretIndex = box.Text.Length;
    }

    private static void OnLeave(object sender, KeyboardFocusChangedEventArgs e)
    {
        var box = (TextBox)sender;
        if (Parse.Time(box.Text) is { } t && Parse.Time(t) != box.Text) box.Text = Parse.Time(t);
    }
}
