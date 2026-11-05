using System.ComponentModel;
using System.Windows.Controls;

namespace AzurLaneDashboard.App.Views;

internal static class DataGridSortBridge
{
    public static void Apply(
        DataGrid dataGrid,
        DataGridSortingEventArgs e,
        IReadOnlyDictionary<string, string> sortMap,
        Action<string, bool> applySort)
    {
        var key = string.IsNullOrWhiteSpace(e.Column.SortMemberPath)
            ? e.Column.Header?.ToString() ?? string.Empty
            : e.Column.SortMemberPath;
        e.Handled = true;
        if (!sortMap.TryGetValue(key, out var sortOption))
        {
            return;
        }

        var ascending = e.Column.SortDirection != ListSortDirection.Ascending;
        foreach (var column in dataGrid.Columns)
        {
            column.SortDirection = null;
        }

        e.Column.SortDirection = ascending ? ListSortDirection.Ascending : ListSortDirection.Descending;
        applySort(sortOption, ascending);
    }
}
