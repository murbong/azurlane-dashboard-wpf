using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private AuctionDashboard auction = new();

    public AuctionDashboard Auction
    {
        get => auction;
        private set => SetProperty(ref auction, value);
    }
}
