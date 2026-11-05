using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AzurLaneDashboard.App.ViewModels;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.Views;

public partial class LevelingPlanView : UserControl
{
    // Same breakpoint as the other 편성·육성 tabs; stacked, the fleet stays first and the queue follows.
    private const double StackedLayoutWidth = 900;

    public LevelingPlanView()
    {
        InitializeComponent();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < StackedLayoutWidth;
        GapColumn.Width = new GridLength(stacked ? 0 : 20);
        SideColumn.MinWidth = stacked ? 0 : 360;
        SideColumn.Width = stacked ? new GridLength(0) : new GridLength(2, GridUnitType.Star);
        Grid.SetColumn(SideSection, stacked ? 0 : 2);
        Grid.SetRow(SideSection, stacked ? 1 : 0);
        Grid.SetRow(FleetSection, 0);
        SideSection.Margin = new Thickness(0, stacked ? 20 : 0, 0, 0);
    }

    // Hovering a queue row outlines the same ship in the fleet.
    private void OnQueueEnter(object sender, MouseEventArgs e) => Highlight(sender, true);

    private void OnQueueLeave(object sender, MouseEventArgs e) => Highlight(sender, false);

    private void Highlight(object sender, bool on)
    {
        if (DataContext is not FleetPlannerViewModel model ||
            sender is not FrameworkElement { DataContext: LevelingQueueRow row })
            return;
        if (on) model.HighlightedShipUid = row.ShipUid;
        else if (model.HighlightedShipUid == row.ShipUid) model.HighlightedShipUid = "";
    }
}
