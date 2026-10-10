using System.Globalization;
using System.Windows.Data;
using WolfEQ.Models;

namespace WolfEQ.Controls;

/// <summary>Shows filter types as readable names ("Low shelf") wherever an EqFilterType is displayed.</summary>
public sealed class EqFilterTypeNameConverter : IValueConverter
{
    public static string ToDisplayName(EqFilterType type) => type switch
    {
        EqFilterType.Peak => "Peak",
        EqFilterType.LowShelf => "Low shelf",
        EqFilterType.HighShelf => "High shelf",
        EqFilterType.BandPass => "Band pass",
        EqFilterType.LowPass => "Low pass",
        EqFilterType.HighPass => "High pass",
        EqFilterType.AllPass => "All pass",
        _ => type.ToString()
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is EqFilterType type ? ToDisplayName(type) : value?.ToString() ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
