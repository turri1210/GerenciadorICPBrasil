using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Data;
using GerenciadorIcpBrasil.Modules.ConfigAuditoria.Models;

namespace GerenciadorIcpBrasil.Modules.ConfigAuditoria.Converters;

public class StatusToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is ConfigurationStatus status)
        {
            var expected = ParseParameters(parameter);
            return expected.Contains(status);
        }

        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
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
