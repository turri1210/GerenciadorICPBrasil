using System;
using System.Globalization;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using GerenciadorIcpBrasil.Modules.ConfigAuditoria.Models;

namespace GerenciadorIcpBrasil.Modules.ConfigAuditoria.Converters;

public class StatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush CompliantBrush = new(Color.FromArgb(255, 34, 197, 94)); // green
    private static readonly SolidColorBrush NonCompliantBrush = new(Color.FromArgb(255, 239, 68, 68)); // red
    private static readonly SolidColorBrush PendingBrush = new(Color.FromArgb(255, 250, 204, 21)); // amber
    private static readonly SolidColorBrush UnknownBrush = new(Color.FromArgb(255, 209, 213, 219)); // gray

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is ConfigurationStatus status
            ? status switch
            {
                ConfigurationStatus.Compliant => CompliantBrush,
                ConfigurationStatus.NonCompliant => NonCompliantBrush,
                ConfigurationStatus.Pending => PendingBrush,
                _ => UnknownBrush
            }
            : UnknownBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
