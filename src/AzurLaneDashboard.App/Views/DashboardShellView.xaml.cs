using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AzurLaneDashboard.App.Views;

public partial class DashboardShellView : UserControl
{
    public DashboardShellView() => InitializeComponent();

    /// <summary>Focuses whichever search bar the current page shows: in the toolbar or under its tabs.</summary>
    public void FocusSearch() => SearchBars(this).FirstOrDefault(bar => bar.IsVisible)?.FocusSearch();

    private static IEnumerable<InventorySearchBar> SearchBars(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is InventorySearchBar bar) yield return bar;
            foreach (var nested in SearchBars(child)) yield return nested;
        }
    }
}
