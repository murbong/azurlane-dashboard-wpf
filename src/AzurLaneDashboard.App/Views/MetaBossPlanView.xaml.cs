using System.Windows;
using System.Windows.Controls;

namespace AzurLaneDashboard.App.Views;

public partial class MetaBossPlanView : UserControl
{
    // Same breakpoint as the other 편성·육성 tabs: below it the gear panel moves above the fleet.
    private const double StackedLayoutWidth = 900;

    public MetaBossPlanView()
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

        // The fight list moves above the selected fight's detail the same way.
        FightGapColumn.Width = new GridLength(stacked ? 0 : 20);
        FightListColumn.MinWidth = stacked ? 0 : 280;
        FightDetailColumn.Width = stacked ? new GridLength(0) : new GridLength(3, GridUnitType.Star);
        Grid.SetColumn(FightDetailSection, stacked ? 0 : 2);
        Grid.SetRow(FightDetailSection, stacked ? 1 : 0);
        FightDetailSection.Margin = new Thickness(0, stacked ? 16 : 0, 0, 0);
        FightListSection.MaxHeight = stacked ? 320 : double.PositiveInfinity;
    }
}
