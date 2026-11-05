using System.Globalization;
using System.Windows.Data;

namespace AzurLaneDashboard.App.Converters;

/// <summary>
/// Shows "-" for zero so empty dashboards read as "no data yet" instead of a wall of zeros.
/// The converter parameter, when given, is a prefix such as "R" for auction rounds.
/// </summary>
public sealed class NumberOrDashConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var number = value switch
        {
            int whole => whole,
            long whole => whole,
            double real => real,
            _ => 0d
        };
        return number == 0
            ? "-"
            : $"{parameter as string}{number.ToString("N0", culture)}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
