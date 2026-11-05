using System.Globalization;
using System.Windows;
using System.Windows.Data;
using AzurLaneDashboard.App.ViewModels;

namespace AzurLaneDashboard.App.Converters;

/// <summary>
/// Looks a ship up in the 쩔 대기열 for cards outside it. Values: [ship uid, <see cref="GrowthQueueIndex"/>].
/// Parameter: "row" the queue row (or null), "isgoal" / "isgoalvisible" whether it is pinned, "rank" / "plan" /
/// "rankplan" its texts, "present" / "missing" a visibility for whether the ship is in the queue.
/// </summary>
public sealed class QueueInfoConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var uid = values.Length > 0 ? values[0] as string : null;
        var index = values.Length > 1 ? values[1] as GrowthQueueIndex : null;
        var row = string.IsNullOrEmpty(uid) || index is null ? null : index.Row(uid);
        return (parameter as string) switch
        {
            "isgoal" => row?.IsGoal == true,
            "isgoalvisible" => row?.IsGoal == true ? Visibility.Visible : Visibility.Collapsed,
            "rank" => row?.RankText ?? "",
            "plan" => row?.PlanText ?? "",
            // Third value: the card's own cost text, shown when the ship is not in the queue.
            "rankplan" => row is null
                ? values.Length > 2 ? values[2] as string ?? "" : ""
                : $"{row.RankText} · {row.PlanText}",
            "present" => row is null ? Visibility.Collapsed : Visibility.Visible,
            "missing" => row is null ? Visibility.Visible : Visibility.Collapsed,
            _ => row
        };
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
