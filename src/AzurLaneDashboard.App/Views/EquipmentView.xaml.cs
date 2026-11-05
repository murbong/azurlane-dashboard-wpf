using System.Windows.Controls;
using AzurLaneDashboard.App.ViewModels;

namespace AzurLaneDashboard.App.Views;

public partial class EquipmentView : UserControl
{
    private static readonly IReadOnlyDictionary<string, string> EquipmentSortMap = new Dictionary<string, string>
    {
        ["Score"] = "점수",
        ["Name"] = "이름",
        ["RarityTier"] = "레어도",
        ["EnhanceLevel"] = "강화",
        ["Count"] = "개수",
        ["BagCount"] = "창고",
        ["EquippedCount"] = "장착"
    };

    public EquipmentView()
    {
        InitializeComponent();
    }

    private void EquipmentDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        ApplyEquipmentSort(sender, e);
    }

    private void EquipmentRecommendationsDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        ApplyEquipmentSort(sender, e);
    }

    private void ApplyEquipmentSort(object sender, DataGridSortingEventArgs e)
    {
        if (sender is DataGrid dataGrid && DataContext is MainWindowViewModel viewModel)
        {
            DataGridSortBridge.Apply(dataGrid, e, EquipmentSortMap, viewModel.ApplyEquipmentGridSort);
        }
    }
}
