using System.Globalization;
using System.Windows.Data;

namespace CentreSoutien.Desktop.Views;

public static class Sizes
{
    /// <summary>Window height minus a margin, so dialogs never exceed the window.</summary>
    public static readonly IValueConverter MinusMargin = new Subtract(80);

    private sealed class Subtract(double amount) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is double d ? Math.Max(200, d - amount) : double.PositiveInfinity;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
