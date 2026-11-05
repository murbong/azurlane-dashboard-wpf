using System.Globalization;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>
/// Orders this month's 코어 월간 goods by what this account would use, not by a fixed community list
/// (there is none worth trusting): equipment the recommended fleets would equip, chips the leveling
/// queue's awakenings still lack, ships not yet owned, then spare-currency buys.
/// </summary>
public static class MonthlyShopRanker
{
    /// <summary>A Regal rating at or above S makes a missing ship worth buying, not just collecting.</summary>
    public const int StrongShipScore = 44;

    public static MonthlyShopDashboard Rank(
        IReadOnlyList<MonthlyShopCandidate> goods, int month, long balance, bool hasBalance, bool hasPurchaseRecord)
    {
        var judged = goods.Select(good => (Good: good, Tier: Tier(good, out var reason), Reason: reason)).ToList();
        var ordered = judged
            .OrderBy(item => item.Tier)
            .ThenBy(item => WithinTier(item.Good, item.Tier))
            .ThenByDescending(item => item.Good.GearSlots)
            .ThenByDescending(item => item.Tier == 3 ? item.Good.FleetTechPoints : 0)
            .ThenByDescending(item => item.Good.ShipRatingScore)
            .ThenBy(item => item.Good.Kind == MonthlyShopKind.EquipmentBox ? -item.Good.Price : item.Good.Price)
            .ThenBy(item => item.Good.Order)
            .ThenBy(item => item.Good.GoodId)
            .ToList();

        var rows = new List<MonthlyShopRow>();
        long cumulative = 0;
        foreach (var (good, tier, reason) in ordered)
        {
            var buys = tier is >= 1 and <= 3;
            if (buys) cumulative += (long)good.Price * good.Remaining;
            rows.Add(new MonthlyShopRow
            {
                Rank = rows.Count + 1,
                Good = good,
                Tier = tier,
                Reason = reason,
                CumulativeCost = buys ? cumulative : 0,
                IsWithinBudget = buys && hasBalance && cumulative <= balance,
                HasBudget = hasBalance
            });
        }

        return new MonthlyShopDashboard
        {
            Month = month,
            Balance = balance,
            HasBalance = hasBalance,
            HasPurchaseRecord = hasPurchaseRecord,
            Rows = rows
        };
    }

    private static int Tier(MonthlyShopCandidate good, out string reason)
    {
        if (good.Remaining == 0)
        {
            reason = "이번 달 한도까지 샀습니다";
            return 9;
        }

        switch (good.Kind)
        {
            case MonthlyShopKind.Equipment when good.GearSlots > 0:
                reason = $"추천 편성 {good.GearSlots}칸이 더 좋아짐" +
                         (good.GearExample.Length > 0 ? $" · 예) {good.GearExample}" : "");
                return 1;
            case MonthlyShopKind.Equipment:
                reason = good.OwnedCopies > 0
                    ? $"보유 {good.OwnedCopies}개 · 추천 편성에 더 필요 없음"
                    : "추천 편성에 쓰이지 않음";
                return 4;
            case MonthlyShopKind.CognitiveChip when good.ChipNeed > good.ChipStock:
                reason = string.Create(CultureInfo.CurrentCulture,
                    $"쩔 대기열 각성에 {good.ChipNeed - good.ChipStock:N0}개 부족 · 보유 {good.ChipStock:N0}");
                return 1;
            case MonthlyShopKind.CognitiveChip:
                reason = string.Create(CultureInfo.CurrentCulture,
                    $"보유 {good.ChipStock:N0} · 대기열 각성({good.ChipNeed:N0})에 충분, 값이 싸서 남는 코어로");
                return 3;
            case MonthlyShopKind.Ship when !good.ShipOwned && good.ShipRatingScore >= StrongShipScore:
                reason = $"미보유 · 커뮤니티 최고 {good.ShipRatingText}" + TechText(good);
                return 2;
            case MonthlyShopKind.Ship when !good.ShipOwned:
                reason = "미보유 · 도감" + TechText(good) +
                         (good.ShipRatingText.Length > 0 ? $" · 최고 {good.ShipRatingText}" : "");
                return 3;
            case MonthlyShopKind.Ship:
                reason = "이미 보유";
                return 4;
            case MonthlyShopKind.EquipmentBox:
                reason = "장비 강화 재료 · 남는 코어로";
                return 3;
            default:
                reason = "";
                return 4;
        }
    }

    private static string TechText(MonthlyShopCandidate good) =>
        good.FleetTechPoints > 0 ? $" · 함대 기술 +{good.FleetTechPoints}pt" : "";

    // Inside "여유 있으면": chips (cheapest per use), then missing ships (lasting fleet tech), then boxes.
    private static int WithinTier(MonthlyShopCandidate good, int tier) => tier != 3
        ? 0
        : good.Kind switch
        {
            MonthlyShopKind.CognitiveChip => 0,
            MonthlyShopKind.Ship => 1,
            _ => 2
        };
}
