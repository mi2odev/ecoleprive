using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CentreSoutien.Desktop.Converters;

namespace CentreSoutien.Desktop.Controls;

/// <summary>Round avatar: photo when available, otherwise initials on the sunk colour.</summary>
public sealed class Avatar : Border
{
    public static readonly DependencyProperty InitialsProperty = DependencyProperty.Register(
        nameof(Initials), typeof(string), typeof(Avatar), new PropertyMetadata("", (d, _) => ((Avatar)d).Update()));

    public static readonly DependencyProperty ImagePathProperty = DependencyProperty.Register(
        nameof(ImagePath), typeof(string), typeof(Avatar), new PropertyMetadata(null, (d, _) => ((Avatar)d).Update()));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Avatar), new PropertyMetadata(32.0, (d, _) => ((Avatar)d).Update()));

    private static readonly PathToImageConverter Images = new();
    private readonly TextBlock _text = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };

    public Avatar()
    {
        SetResourceReference(BackgroundProperty, "SunkBrush");
        SetResourceReference(BorderBrushProperty, "LineBrush");
        _text.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        BorderThickness = new Thickness(1);
        ClipToBounds = true;
        Update();
    }

    public string Initials { get => (string)GetValue(InitialsProperty); set => SetValue(InitialsProperty, value); }
    public string? ImagePath { get => (string?)GetValue(ImagePathProperty); set => SetValue(ImagePathProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    private void Update()
    {
        Width = Height = Size;
        CornerRadius = new CornerRadius(Size / 2);
        var image = Images.Convert(ImagePath, typeof(ImageSource), null, System.Globalization.CultureInfo.CurrentCulture) as ImageSource;
        if (image is not null)
        {
            Child = new Border
            {
                CornerRadius = new CornerRadius(Size / 2),
                Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill },
            };
        }
        else
        {
            _text.Text = Initials;
            _text.FontSize = Math.Max(10, Size * 0.34);
            Child = _text;
        }
    }
}
