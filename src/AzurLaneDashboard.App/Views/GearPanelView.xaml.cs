using System.Windows;
using System.Windows.Controls;
using AzurLaneDashboard.App.ViewModels;

namespace AzurLaneDashboard.App.Views;

public partial class GearPanelView : UserControl
{
    public static readonly DependencyProperty PanelProperty = DependencyProperty.Register(
        nameof(Panel), typeof(GearPanel), typeof(GearPanelView), new PropertyMetadata(GearPanel.Empty));

    public GearPanelView()
    {
        InitializeComponent();
    }

    /// <summary>The selected ship's recommendation; the DataContext stays the FleetPlannerViewModel.</summary>
    public GearPanel Panel
    {
        get => (GearPanel)GetValue(PanelProperty);
        set => SetValue(PanelProperty, value);
    }
}
