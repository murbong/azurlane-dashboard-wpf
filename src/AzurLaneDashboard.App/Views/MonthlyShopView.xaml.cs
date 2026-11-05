using System.Windows;
using System.Windows.Controls;

namespace AzurLaneDashboard.App.Views;

public partial class MonthlyShopView : UserControl
{
    // Room for the vertical scrollbar and the grid border, so the last column is never clipped.
    private const double ScrollGutter = 24;

    public MonthlyShopView()
    {
        InitializeComponent();
    }

    // A grouped DataGrid keeps a star column at the width of an earlier layout (as on the growth queue),
    // so the reason column takes whatever the fixed columns leave, in pixels.
    private void ShopGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged) return;
        var reasons = ReasonColumn;
        var fixedWidth = ShopGrid.Columns.Where(column => !ReferenceEquals(column, reasons))
            .Sum(column => column.Width.Value);
        reasons.Width = new DataGridLength(Math.Max(reasons.MinWidth, e.NewSize.Width - fixedWidth - ScrollGutter));
    }
}
