using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>보급 상점 › 월간 상점 (코어 월간) goods in the order worth buying this month.</summary>
    public const int MonthlyShopPageIndex = 12;

    private MonthlyShopDashboard monthlyShop = new();

    public MonthlyShopDashboard MonthlyShop
    {
        get => monthlyShop;
        private set => SetProperty(ref monthlyShop, value);
    }
}
