using System.Globalization;

namespace AzurLaneDashboard.Core.Models;

public enum MonthlyShopKind
{
    Equipment,
    Ship,
    CognitiveChip,
    EquipmentBox,
    Other
}

/// <summary>One good of this month's 코어 월간 list with what the account says about it.</summary>
public sealed record MonthlyShopCandidate
{
    public int GoodId { get; init; }
    public MonthlyShopKind Kind { get; init; }
    public int CommodityId { get; init; }
    public string Name { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public int Num { get; init; } = 1;
    public int Price { get; init; }
    public int Limit { get; init; }
    public int Order { get; init; }
    public int Bought { get; init; }

    /// <summary>Fleet slots of the recommended fleets this piece would improve (equipment only).</summary>
    public int GearSlots { get; init; }
    /// <summary>One such slot, e.g. "괴츠 폰 베를리힝겐 1번 칸 (META 보스전)".</summary>
    public string GearExample { get; init; } = "";
    /// <summary>Copies already in the warehouse or on ships (equipment only).</summary>
    public int OwnedCopies { get; init; }

    public bool ShipOwned { get; init; }
    public int ShipRatingScore { get; init; }
    public string ShipRatingText { get; init; } = "";
    /// <summary>Fleet tech points the account gets for obtaining the ship once (ships only).</summary>
    public int FleetTechPoints { get; init; }

    /// <summary>Cognitive chips the leveling queue needs for its awakenings, and how many are on hand.</summary>
    public long ChipNeed { get; init; }
    public long ChipStock { get; init; }

    public int Remaining => Math.Max(0, Limit - Bought);
}

public sealed class MonthlyShopRow
{
    public int Rank { get; init; }
    public MonthlyShopCandidate Good { get; init; } = new();
    /// <summary>1 필요, 2 권장, 3 여유 있으면, 4 필요 없음, 9 구매 완료.</summary>
    public int Tier { get; init; }
    public string Reason { get; init; } = "";
    public long CumulativeCost { get; init; }
    public bool IsWithinBudget { get; init; }
    /// <summary>The core data balance is known, so the budget column can say yes or no.</summary>
    public bool HasBudget { get; init; }

    public string Name => Good.Num > 1 ? $"{Good.Name} ×{Good.Num}" : Good.Name;
    public string IconUrl => Good.IconUrl;
    public string Rarity => Good.Rarity;
    public string KindText => Good.Kind switch
    {
        MonthlyShopKind.Equipment => "장비",
        MonthlyShopKind.Ship => "함선",
        MonthlyShopKind.CognitiveChip => "각성 재료",
        MonthlyShopKind.EquipmentBox => "장비 상자",
        _ => "기타"
    };
    public string TierText => Tier switch
    {
        1 => "필요",
        2 => "권장",
        3 => "여유 있으면",
        9 => "구매 완료",
        _ => "필요 없음"
    };
    /// <summary>"need", "suggest", "spare", "skip" or "done", for the chip colour.</summary>
    public string TierKind => Tier switch
    {
        1 => "need",
        2 => "suggest",
        3 => "spare",
        9 => "done",
        _ => "skip"
    };
    /// <summary>What the group of this tier means, for the group header.</summary>
    public string TierNote => Tier switch
    {
        1 => "추천 편성이나 쩔 대기열에 바로 쓰입니다",
        2 => "없는 함선 중 커뮤니티 평가가 S 이상입니다",
        3 => "당장 쓰이진 않지만 오래 남는 것부터",
        9 => "이번 달 한도까지 산 품목",
        _ => "이미 있거나 추천 편성에 쓰이지 않습니다"
    };
    public bool IsDone => Tier == 9;
    /// <summary>Rows not worth buying this month, drawn faded.</summary>
    public bool IsDimmed => Tier is 4 or 9;
    public bool IsBuy => Tier is >= 1 and <= 3;
    public string PriceText => string.Create(CultureInfo.CurrentCulture, $"{Good.Price:N0}");
    public string CumulativeText => IsBuy
        ? string.Create(CultureInfo.CurrentCulture, $"누적 {CumulativeCost:N0}")
        : "";
    public string StockText => $"{Good.Bought}/{Good.Limit}";
    public string BudgetStatusText => !IsBuy || !HasBudget ? "" : IsWithinBudget ? "살 수 있음" : "코어 부족";
    /// <summary>"good" within the balance, "short" past it, "" when the column says nothing.</summary>
    public string BudgetKind => BudgetStatusText.Length == 0 ? "" : IsWithinBudget ? "good" : "short";
}

public sealed class MonthlyShopDashboard
{
    public int Month { get; init; }
    public long Balance { get; init; }
    public bool HasBalance { get; init; }
    public bool HasPurchaseRecord { get; init; }
    public IReadOnlyList<MonthlyShopRow> Rows { get; init; } = [];

    public bool HasRows => Rows.Count > 0;
    public bool HasNoRows => !HasRows;
    public int BudgetRowCount => Rows.Count(row => row.IsWithinBudget);
    public long PlannedCost => Rows.Where(row => row.Tier is >= 1 and <= 3)
        .Sum(row => (long)row.Good.Price * row.Good.Remaining);
    public int BuyRowCount => Rows.Count(row => row.IsBuy);
    public string MonthText => Month > 0 ? $"{Month}월 코어 월간" : "코어 월간";
    public string BalanceValueText => HasBalance ? string.Create(CultureInfo.CurrentCulture, $"{Balance:N0}") : "-";
    public string BalanceText => HasBalance
        ? string.Create(CultureInfo.CurrentCulture, $"코어 데이터 {Balance:N0}")
        : "코어 데이터 보유량 미수집";
    public string BudgetText => !HasBalance
        ? "보유량을 모르면 예산 표시를 하지 않습니다"
        : string.Create(CultureInfo.CurrentCulture,
            $"추천 품목 전부 {PlannedCost:N0} · 지금 코어로 위에서부터 {BudgetRowCount}개까지");
    public string RecordText => HasPurchaseRecord
        ? "이번 달 구매 기록 반영"
        : "이번 달 구매 기록 없음 · 게임에서 월간 상점을 한 번 열면 반영됩니다";
}
