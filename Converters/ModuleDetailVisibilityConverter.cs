using GerenciadorIcpBrasil.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace GerenciadorIcpBrasil.Converters;

public sealed class ModuleDetailVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is ModuleItem module
               && !string.Equals(module.Id, "leitor-certificado", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
