using System.Windows;
using System.Windows.Controls;

namespace AzurLaneDashboard.App.Views;

public partial class MeowfficerView : UserControl
{
    // Same breakpoint as the 편성·육성 tabs: below it the talent card moves under the list.
    private const double StackedLayoutWidth = 900;

    public MeowfficerView()
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
        Grid.SetRow(SideSection, stacked ? 1 : 0);
        SideSection.Margin = new Thickness(0, stacked ? 16 : 0, 0, 0);
    }
}
