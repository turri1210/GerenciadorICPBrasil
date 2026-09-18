using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using GerenciadorIcpBrasil.Models;

namespace GerenciadorIcpBrasil.Converters;

public sealed class ModulePanelVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not ModuleItem module)
        {
            return Visibility.Collapsed;
        }

        var targetId = parameter as string;
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return Visibility.Collapsed;
        }

        return module.IsInstalled && string.Equals(module.Id, targetId, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
