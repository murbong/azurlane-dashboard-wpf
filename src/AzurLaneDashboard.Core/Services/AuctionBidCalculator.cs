using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

public static class AuctionBidCalculator
{
    private const double DividendRate = 0.15;
    private const int RivalCount = 3;
    private const long SearchStep = 1_000;

    private static readonly IReadOnlyDictionary<string, (long Low, long High)[]> BotRanges =
        new Dictionary<string, (long, long)[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["B"] =
            [
                (101_625, 211_950), (132_275, 282_600), (192_925, 343_250),
                (263_575, 413_900), (404_225, 474_550)
            ],
            ["A"] =
            [
                (341_000, 683_200), (465_400, 907_600), (689_800, 1_052_000),
                (824_200, 1_366_400), (1_078_600, 1_630_800)
            ],
            ["S"] =
            [
                (1_406_250, 2_153_500), (1_820_750, 2_738_000), (1_855_250, 3_572_500),
                (2_789_750, 4_407_000), (3_524_250, 5_041_500)
            ]
        };

    public static AuctionBidRecommendationRow Calculate(
        string session,
        int round,
        long minimumValue,
        long expectedValue,
        long observedHighBid)
    {
        if (!BotRanges.TryGetValue(session, out var ranges) || expectedValue <= 0)
        {
            return new AuctionBidRecommendationRow
            {
                Session = session,
                SafetyCeiling = Math.Max(0, minimumValue),
                BreakEvenCeiling = Math.Max(0, expectedValue),
                Guidance = "공개 정보가 더 필요합니다."
            };
        }

        var range = ranges[Math.Clamp(round <= 0 ? 1 : round, 1, ranges.Length) - 1];
        var low = Math.Max(range.Low, observedHighBid);
        var high = Math.Max(range.High, low + SearchStep);
        var expectedRivalMax = low + RivalCount * (high - low) / (RivalCount + 1);
        var losingDividend = expectedRivalMax * DividendRate;

        long bestBid = 0;
        var bestUtility = losingDividend;
        var searchLimit = Math.Min(expectedValue, high + SearchStep);
        for (var bid = SearchStep; bid <= searchLimit; bid += SearchStep)
        {
            var probability = WinProbability(bid, low, high);
            var utility = probability * (expectedValue - bid) + (1 - probability) * losingDividend;
            if (utility > bestUtility)
            {
                bestUtility = utility;
                bestBid = bid;
            }
        }

        var winProbability = WinProbability(bestBid, low, high);
        return new AuctionBidRecommendationRow
        {
            Session = session,
            RecommendedBid = bestBid,
            SafetyCeiling = Math.Max(0, minimumValue),
            BreakEvenCeiling = expectedValue,
            WinProbability = winProbability,
            ExpectedNetValue = (long)Math.Round(bestUtility),
            Guidance = bestBid == 0
                ? "배당 기대값이 더 높아 관망 권장"
                : $"R{Math.Clamp(round <= 0 ? 1 : round, 1, 5)} 봇 범위·배당 15% 반영"
        };
    }

    private static double WinProbability(long bid, long low, long high)
    {
        if (bid <= low)
        {
            return 0;
        }

        if (bid >= high)
        {
            return 1;
        }

        var singleRival = (double)(bid - low) / (high - low);
        return Math.Pow(singleRival, RivalCount);
    }
}
