using Microsoft.UI.Xaml.Data;

namespace GerenciadorIcpBrasil.Converters;

public sealed class BooleanNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => !(value is bool flag && flag);

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => !(value is bool flag && flag);
}
