using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AzurLaneDashboard.App.ViewModels;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.Views;

public partial class FleetPlannerView : UserControl
{
    // Below this width the side column would squeeze the three-slot fleet rows, so the
    // growth list moves above the fleets: the next action stays visible without scrolling.
    private const double StackedLayoutWidth = 900;

    public FleetPlannerView()
    {
        InitializeComponent();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < StackedLayoutWidth;
        GapColumn.Width = new GridLength(stacked ? 0 : 20);
        SideColumn.MinWidth = stacked ? 0 : 340;
        SideColumn.Width = stacked ? new GridLength(0) : new GridLength(2, GridUnitType.Star);
        Grid.SetColumn(SideSection, stacked ? 0 : 2);
        Grid.SetRow(SideSection, 0);
        Grid.SetRow(FleetSection, stacked ? 1 : 0);
        FleetSection.Margin = new Thickness(0, stacked ? 20 : 0, 0, 0);
    }

    private void OnCandidateEnter(object sender, MouseEventArgs e) => Highlight(sender, true);

    private void OnCandidateLeave(object sender, MouseEventArgs e) => Highlight(sender, false);

    private void OnCandidateFocusChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        Highlight(sender, e.NewValue is true);

    private void Highlight(object sender, bool on)
    {
        if (DataContext is not FleetPlannerViewModel model ||
            sender is not FrameworkElement { DataContext: FleetPlanCandidate candidate })
            return;
        if (on) model.HighlightedShipUid = candidate.ReplacesUid;
        else if (model.HighlightedShipUid == candidate.ReplacesUid) model.HighlightedShipUid = "";
    }
}
