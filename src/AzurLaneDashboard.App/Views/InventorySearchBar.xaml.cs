using System.Windows.Controls;

namespace AzurLaneDashboard.App.Views;

public partial class InventorySearchBar : UserControl
{
    public InventorySearchBar()
    {
        InitializeComponent();
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
}
