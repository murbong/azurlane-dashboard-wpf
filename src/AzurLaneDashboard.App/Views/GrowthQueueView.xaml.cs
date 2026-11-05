using System.Windows;
using System.Windows.Controls;

namespace AzurLaneDashboard.App.Views;

public partial class GrowthQueueView : UserControl
{
    // Room for the vertical scrollbar and the grid border, so the last column is never clipped.
    private const double ScrollGutter = 24;

    public GrowthQueueView()
    {
        InitializeComponent();
    }

    // A grouped DataGrid keeps a star column at the width of an earlier layout, so the reason column is
    // sized in pixels from the grid's new width instead: it takes whatever the fixed columns leave.
    private void QueueGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged) return;
        var reasons = ReasonColumn;
        var fixedWidth = QueueGrid.Columns.Where(column => !ReferenceEquals(column, reasons))
            .Sum(column => column.Width.Value);
        reasons.Width = new DataGridLength(Math.Max(reasons.MinWidth, e.NewSize.Width - fixedWidth - ScrollGutter));
    }
}
