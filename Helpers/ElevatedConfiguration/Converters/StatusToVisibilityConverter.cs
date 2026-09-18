using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ConfigAuditoria.Models;

namespace ConfigAuditoria.Converters;

public class StatusToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ConfigurationStatus status)
        {
            var expectedStatuses = ParseParameters(parameter);
            return expectedStatuses.Contains(status)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static IReadOnlyCollection<ConfigurationStatus> ParseParameters(object? parameter)
    {
        if (parameter is string statusNames)
        {
            var parts = statusNames.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var statuses = new List<ConfigurationStatus>();

            foreach (var part in parts)
            {
                if (Enum.TryParse(part, true, out ConfigurationStatus parsedStatus))
                {
                    statuses.Add(parsedStatus);
                }
            }

            if (statuses.Count > 0)
            {
                return statuses;
            }
        }

        if (parameter is ConfigurationStatus status)
        {
            return new[] { status };
        }

        return new[] { ConfigurationStatus.NonCompliant };
    }
}
