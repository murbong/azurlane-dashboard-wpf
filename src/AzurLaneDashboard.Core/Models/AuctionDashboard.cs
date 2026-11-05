using System.Collections.ObjectModel;

namespace AzurLaneDashboard.Core.Models;

public sealed class AuctionDashboard
{
    public string Status { get; init; } = "경매 정보 대기 중";
    public string Detail { get; init; } = "경매에 입장하면 공개 정보가 자동으로 표시됩니다.";
    public string Session { get; init; } = "";
    public int Round { get; init; }
    public long MinimumValue { get; init; }
    public long ExpectedValue { get; init; }
    public long MaximumValue { get; init; }
    public long LatestHighBid { get; init; }
    public long WinningBid { get; init; }
    public long ActualValue { get; init; }
    public long Profit { get; init; }
    public int PublicFactCount { get; init; }
    public ObservableCollection<AuctionItemRow> Items { get; init; } = [];
    public ObservableCollection<AuctionBidRecommendationRow> Recommendations { get; init; } = [];

    // Without any public estimate every row is zero; one explanatory line reads better than a zero table.
    public bool HasEstimates => Recommendations.Any(row => row.BreakEvenCeiling > 0 || row.RecommendedBid > 0);
    public bool HasNoEstimates => !HasEstimates;
}

public sealed class AuctionItemRow
{
    public int Uid { get; init; }
    public int ItemId { get; init; }
    public string Name { get; init; } = "미확인 물품";
    public string RarityText { get; init; } = "미공개";
    public string ContourText { get; init; } = "미공개";
    public string Evidence { get; init; } = "";
    public long MinimumValue { get; init; }
    public long ExpectedValue { get; init; }
    public long MaximumValue { get; init; }
    public bool IsExact { get; init; }
}

public sealed class AuctionBidRecommendationRow
{
    public string Session { get; init; } = "";
    public long RecommendedBid { get; init; }
    public long SafetyCeiling { get; init; }
    public long BreakEvenCeiling { get; init; }
    public double WinProbability { get; init; }
    public long ExpectedNetValue { get; init; }
    public string Guidance { get; init; } = "";
}
