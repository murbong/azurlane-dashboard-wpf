using System.Windows.Controls;
using AzurLaneDashboard.App.ViewModels;

namespace AzurLaneDashboard.App.Views;

public partial class DockView : UserControl
{
    private static readonly IReadOnlyDictionary<string, string> ShipSortMap = new Dictionary<string, string>
    {
        ["DisplayName"] = "이름",
        ["Level"] = "Lv",
        ["MoodText"] = "기분",
        ["CreateTimeText"] = "입수시간",
        ["Rarity"] = "레어도",
        ["CommunityBestScore"] = "커뮤니티"
    };

    public DockView()
    {
        InitializeComponent();
    }

    private void ShipsDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (sender is DataGrid dataGrid && DataContext is MainWindowViewModel viewModel)
        {
            DataGridSortBridge.Apply(dataGrid, e, ShipSortMap, viewModel.ApplyDockGridSort);
        }
    }
}
