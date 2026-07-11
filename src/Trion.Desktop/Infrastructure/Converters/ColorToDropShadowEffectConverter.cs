using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Trion.Desktop.Infrastructure.Converters;

[ValueConversion(typeof(Color), typeof(DropShadowEffect))]
public sealed class ColorToDropShadowEffectConverter : IValueConverter
{
    public double BlurRadius   { get; set; } = 28;
    public double ShadowDepth  { get; set; } = 0;
    public double Opacity      { get; set; } = 1;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Color color)
            return new DropShadowEffect { Color = color, BlurRadius = BlurRadius, ShadowDepth = ShadowDepth, Opacity = Opacity };
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
