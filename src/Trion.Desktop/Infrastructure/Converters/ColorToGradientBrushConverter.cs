using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Trion.Desktop.Infrastructure.Converters;

[ValueConversion(typeof(Color), typeof(LinearGradientBrush))]
public sealed class ColorToGradientBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Color color) return null;

        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
        brush.GradientStops.Add(new GradientStop(color,              0.3));
        brush.GradientStops.Add(new GradientStop(color,              0.7));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
