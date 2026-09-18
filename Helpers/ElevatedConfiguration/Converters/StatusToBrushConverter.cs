using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ConfigAuditoria.Models;

namespace ConfigAuditoria.Converters;

public class StatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush CompliantBrush = CreateFrozenBrush(Color.FromRgb(34, 197, 94)); // green
    private static readonly SolidColorBrush NonCompliantBrush = CreateFrozenBrush(Color.FromRgb(239, 68, 68)); // red
    private static readonly SolidColorBrush PendingBrush = CreateFrozenBrush(Color.FromRgb(250, 204, 21)); // amber
    private static readonly SolidColorBrush UnknownBrush = CreateFrozenBrush(Color.FromRgb(209, 213, 219)); // gray

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ConfigurationStatus status)
        {
            return status switch
            {
                ConfigurationStatus.Compliant => CompliantBrush,
                ConfigurationStatus.NonCompliant => NonCompliantBrush,
                ConfigurationStatus.Pending => PendingBrush,
                _ => UnknownBrush
            };
        }

        return UnknownBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
