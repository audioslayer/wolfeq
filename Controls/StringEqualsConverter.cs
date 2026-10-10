using System.Globalization;
using System.Windows.Data;

namespace WolfEQ.Controls;

/// <summary>
/// Binds a radio button to a string property: checked when the value equals the parameter,
/// and checking it writes the parameter back.
/// </summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter?.ToString() ?? string.Empty : Binding.DoNothing;
}
