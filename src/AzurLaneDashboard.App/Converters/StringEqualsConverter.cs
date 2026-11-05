using System.Globalization;
using System.Windows.Data;

namespace AzurLaneDashboard.App.Converters;

/// <summary>True when two bound strings are equal and non-empty.</summary>
public sealed class StringEqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is string left && left.Length > 0 &&
        values[1] is string right && string.Equals(left, right, StringComparison.Ordinal);

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
