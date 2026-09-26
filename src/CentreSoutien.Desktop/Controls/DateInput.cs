using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CentreSoutien.Presentation.Core;

namespace CentreSoutien.Desktop.Controls;

/// <summary>
/// Date field that can be typed or picked:
/// type digits ("15032010" → "15/03/2010", slashes are inserted as you type; "15/3/10", "15-03-2010" also work)
/// or click the calendar button. With <see cref="StartWithYears"/> (birth dates) the calendar opens on the
/// year view so a year, then a month, then a day is chosen in three clicks.
/// </summary>
public sealed partial class DateInput : Border
{
    public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(
        nameof(SelectedDate), typeof(DateTime?), typeof(DateInput),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((DateInput)d).ShowSelected()));

    public static readonly DependencyProperty StartWithYearsProperty = DependencyProperty.Register(
        nameof(StartWithYears), typeof(bool), typeof(DateInput), new PropertyMetadata(false));

    /// <summary>Refuse dates after today (birth dates).</summary>
    public static readonly DependencyProperty NoFutureDatesProperty = DependencyProperty.Register(
        nameof(NoFutureDates), typeof(bool), typeof(DateInput), new PropertyMetadata(false));

    /// <summary>True while the typed text is not a valid date. Bind it (OneWayToSource) so the form can refuse to save.</summary>
    public static readonly DependencyProperty HasInvalidTextProperty = DependencyProperty.Register(
        nameof(HasInvalidText), typeof(bool), typeof(DateInput),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private readonly TextBox _text = new()
    {
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        MinHeight = 0,
        Padding = new Thickness(10, 0, 4, 0),
        VerticalContentAlignment = VerticalAlignment.Center,
        Tag = "jj/mm/aaaa",
        ToolTip = "Tapez la date (ex. 15032010 ou 15/03/2010) ou cliquez sur le calendrier",
    };
    private readonly Button _button = new() { Width = 34, Focusable = false, Cursor = Cursors.Hand, ToolTip = "Choisir dans le calendrier" };
    private readonly Popup _popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
    private readonly Calendar _calendar = new() { FirstDayOfWeek = DayOfWeek.Saturday, IsTodayHighlighted = true };
    private bool _updating;
    private bool _invalid;

    public DateInput()
    {
        MinHeight = 36;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        SnapsToDevicePixels = true;
        SetResourceReference(BackgroundProperty, "PanelBrush");
        SetResourceReference(BorderBrushProperty, "LineBrush");

        _button.Content = new TextBlock
        {
            Text = "", // Calendar glyph
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ((TextBlock)_button.Content).SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _button.Template = FlatButtonTemplate();
        _button.Click += (_, _) => OpenCalendar();

        var calendarHost = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 4, 0, 0), Child = _calendar };
        calendarHost.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        calendarHost.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        _popup.Child = calendarHost;
        _popup.PlacementTarget = this;
        _calendar.SelectedDatesChanged += OnCalendarPicked;
        _calendar.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { _popup.IsOpen = false; e.Handled = true; } };

        _text.PreviewTextInput += OnPreviewTextInput;
        _text.TextChanged += (_, _) => OnTyped();
        _text.LostKeyboardFocus += (_, _) => Commit();
        _text.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Commit();
            else if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt) { OpenCalendar(); e.Handled = true; }
        };
        DataObject.AddPastingHandler(_text, (_, _) => Dispatcher.BeginInvoke(OnTyped));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_button, 1);
        grid.Children.Add(_text);
        grid.Children.Add(_button);
        grid.Children.Add(_popup);
        Child = grid;

        GotKeyboardFocus += (_, e) => { if (ReferenceEquals(e.NewFocus, this)) _text.Focus(); };
        Focusable = false;
    }

    public DateTime? SelectedDate { get => (DateTime?)GetValue(SelectedDateProperty); set => SetValue(SelectedDateProperty, value); }
    public bool StartWithYears { get => (bool)GetValue(StartWithYearsProperty); set => SetValue(StartWithYearsProperty, value); }
    public bool NoFutureDates { get => (bool)GetValue(NoFutureDatesProperty); set => SetValue(NoFutureDatesProperty, value); }
    public bool HasInvalidText { get => (bool)GetValue(HasInvalidTextProperty); set => SetValue(HasInvalidTextProperty, value); }

    private DateTime? MaxDate => NoFutureDates ? DateTime.Today : null;

    /// <summary>The inner text box (FocusOnShow and Enter behaviours can target it).</summary>
    public TextBox TextBox => _text;

    [GeneratedRegex(@"^\d{2}$|^\d{2}/\d{2}$")]
    private static partial Regex SlashPoint();

    /// <summary>Inserts the slashes while digits are typed at the end: "15" + "0" → "15/0".</summary>
    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Text.Length != 1 || !char.IsDigit(e.Text[0])) return;
        if (_text.SelectionLength > 0 || _text.CaretIndex != _text.Text.Length) return;
        if (_text.Text.Length >= 10) { e.Handled = true; return; }
        if (SlashPoint().IsMatch(_text.Text))
        {
            _text.Text += "/" + e.Text;
            _text.CaretIndex = _text.Text.Length;
            e.Handled = true;
        }
    }

    private void OnTyped()
    {
        if (_updating) return;
        var text = _text.Text.Trim();
        if (text.Length == 0)
        {
            SetInvalid(false);
            SetFromText(null);
            return;
        }
        var parsed = Parse.Date(text);
        var complete = parsed is not null && (text.Length >= 8 || text.Count(c => c is '/' or '-' or '.') == 2);
        if (complete && IsAllowed(parsed!.Value))
        {
            SetInvalid(false);
            SetFromText(parsed);
        }
        else if (!complete)
        {
            // Still typing: don't flag yet, don't change the value.
            SetInvalid(false);
        }
        else
        {
            SetInvalid(true);
        }
    }

    /// <summary>Leaving the field (or Enter): tidy the text, or flag it when it isn't a valid date.</summary>
    private void Commit()
    {
        var text = _text.Text.Trim();
        if (text.Length == 0)
        {
            SetInvalid(false);
            SetFromText(null);
            return;
        }
        var parsed = Parse.Date(text);
        if (parsed is { } d && IsAllowed(d))
        {
            SetInvalid(false);
            SetFromText(d);
            ShowText(d);
        }
        else
        {
            SetInvalid(true);
        }
    }

    private bool IsAllowed(DateTime d) => MaxDate is not { } max || d.Date <= max.Date;

    private void SetFromText(DateTime? value)
    {
        if (Nullable.Equals(SelectedDate, value)) return;
        _updating = true;
        SetCurrentValue(SelectedDateProperty, value);
        GetBindingExpression(SelectedDateProperty)?.UpdateSource();
        _updating = false;
    }

    private void ShowSelected()
    {
        if (_updating) return;
        SetInvalid(false);
        ShowText(SelectedDate);
    }

    private void ShowText(DateTime? value)
    {
        var text = value is { } d ? Parse.Date(d) : "";
        if (_text.Text == text) return;
        _updating = true;
        _text.Text = text;
        _text.CaretIndex = text.Length;
        _updating = false;
    }

    private void SetInvalid(bool invalid)
    {
        _invalid = invalid;
        if (HasInvalidText != invalid)
        {
            SetCurrentValue(HasInvalidTextProperty, invalid);
            GetBindingExpression(HasInvalidTextProperty)?.UpdateSource();
        }
        if (invalid)
        {
            SetResourceReference(BorderBrushProperty, "BadBrush");
            _text.ToolTip = MaxDate is { } max && Parse.Date(_text.Text) is { } d && d > max
                ? $"La date ne peut pas dépasser le {Parse.Date(max)}."
                : "Date invalide. Format attendu : jj/mm/aaaa (ex. 15/03/2010).";
        }
        else
        {
            SetResourceReference(BorderBrushProperty, _text.IsKeyboardFocusWithin ? "AccentBrush" : "LineBrush");
            _text.ToolTip = "Tapez la date (ex. 15032010 ou 15/03/2010) ou cliquez sur le calendrier";
        }
    }

    private void OpenCalendar()
    {
        Commit();
        _updating = true;
        // Clear first: the Calendar throws if its selected date falls outside DisplayDateEnd.
        _calendar.SelectedDate = null;
        _calendar.DisplayDateEnd = MaxDate;
        if (SelectedDate is { } current && (MaxDate is not { } limit || current <= limit)) _calendar.SelectedDate = current;
        var anchor = SelectedDate ?? (StartWithYears ? DateTime.Today.AddYears(-12) : DateTime.Today);
        if (MaxDate is { } max && anchor > max) anchor = max;
        _calendar.DisplayDate = anchor;
        _calendar.DisplayMode = StartWithYears && SelectedDate is null ? CalendarMode.Decade : CalendarMode.Month;
        _updating = false;
        _popup.IsOpen = true;
        _calendar.Focus();
    }

    private void OnCalendarPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_updating || _calendar.SelectedDate is not { } d) return;
        SetInvalid(false);
        SetFromText(d.Date);
        ShowText(d.Date);
        _popup.IsOpen = false;
        _text.Focus();
        _text.CaretIndex = _text.Text.Length;
        // A click inside the calendar keeps the mouse captured; release it so the next click works normally.
        Mouse.Capture(null);
    }

    protected override void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsKeyboardFocusWithinChanged(e);
        if (_invalid) return;
        SetResourceReference(BorderBrushProperty, IsKeyboardFocusWithin ? "AccentBrush" : "LineBrush");
    }

    private static ControlTemplate FlatButtonTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        t.VisualTree = bd;
        return t;
    }
}
