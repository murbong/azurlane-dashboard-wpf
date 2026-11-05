using System.Globalization;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>One gear slot as the 장비 추천 panel shows it: what to equip, how to get it, and what is there now.</summary>
public sealed class GearSlotRow
{
    public int Slot { get; init; }
    /// <summary>The slot cell's top line: the slot number, or "SP" for special equipment.</summary>
    public string SlotText { get; init; } = "";
    public string SlotLabel { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string RankText { get; init; } = "";
    public bool IsOutOfRank { get; init; }
    public string Name { get; init; } = "";
    /// <summary>"move", "move-sortie", "bag", "keep", "hold", "missing" or "nodata".</summary>
    public string ActionKind { get; init; } = "";
    public string ActionText { get; init; } = "";
    public string ActionToolTip { get; init; } = "";
    public string SubText { get; init; } = "";
    public string AltText { get; init; } = "";
    public bool DiffersInAlternateArmor { get; init; }

    public bool HasAlt => AltText.Length > 0;
    public bool HasRarity => Rarity.Length > 0;
    public string IconToolTip => HasRarity ? $"{Name} · {Rarity}" : Name;
}

public static class GearPlanPresenter
{
    /// <summary>The five gear slots, then the special equipment slot when the plan has one.</summary>
    public static IReadOnlyList<GearSlotRow> Rows(
        GearShipPlan plan, IReadOnlyDictionary<int, GearItemInfo> items, GearInventory inventory, GearSourceMode mode,
        string alternateArmorLabel = "", IReadOnlyDictionary<int, GearItemInfo>? augmentItems = null)
    {
        var rows = plan.Slots.Select(slot => Row(slot, items, inventory.Spare, mode,
            plan.SlotsDifferingInAlternateArmor.Contains(slot.Slot), alternateArmorLabel, "커뮤니티 장비 순위 없음",
            slot.Slot.ToString(CultureInfo.InvariantCulture), null)).ToList();
        if (plan.Augment is { } augment)
        {
            var usecase = plan.AugmentUsecase.Length > 0 ? $"커뮤니티 특수장비 순위 · {plan.AugmentUsecase}" : "";
            rows.Add(Row(augment, augmentItems ?? new Dictionary<int, GearItemInfo>(), inventory.AugmentSpare, mode,
                false, alternateArmorLabel, "커뮤니티 특수장비 순위 없음", "SP", ("특수", usecase)));
        }

        return rows;
    }

    public static string FootText(GearShipPlan plan, GearSourceMode mode)
    {
        var changes = plan.ChangeCount;
        if (changes == 0) return "바꿀 칸 없음";
        var moves = plan.AllSlots.Count(slot => slot.Action == GearSlotAction.Move);
        var bags = plan.AllSlots.Count(slot => slot.Action == GearSlotAction.Bag);
        return mode == GearSourceMode.Bag
            ? $"바꿀 칸 {changes} · 창고 장비만"
            : $"바꿀 칸 {changes} · 옮기기 {moves} · 창고 {bags}";
    }

    public static string ArmorNote(GearShipPlan plan, string usecaseArmorLabel, string alternateArmorLabel)
    {
        if (alternateArmorLabel.Length == 0) return "";
        return plan.SlotsDifferingInAlternateArmor.Count == 0
            ? $"{usecaseArmorLabel}·{alternateArmorLabel} 순위 같음"
            : $"{string.Join(", ", plan.SlotsDifferingInAlternateArmor)}번 칸은 {alternateArmorLabel} 순위가 다름";
    }

    private static GearSlotRow Row(
        GearSlotPlan slot, IReadOnlyDictionary<int, GearItemInfo> items, IReadOnlyDictionary<int, IReadOnlyList<int>> spare,
        GearSourceMode mode, bool differs, string alternateArmorLabel, string noDataText, string slotText,
        (string Label, string ToolTip)? special)
    {
        var shown = items.GetValueOrDefault(slot.BaseId);
        var current = slot.Current is null ? null : items.GetValueOrDefault(slot.Current.BaseId);
        var nowText = slot.Current is null
            ? "지금 빈 칸"
            : $"지금 {current?.Name ?? "?"} +{slot.Current.Enhance} · {RankLabel(slot.CurrentRank)}";
        var blockedText = slot.BlockedBaseId > 0
            ? $"{slot.BlockedRank}순위 {items.GetValueOrDefault(slot.BlockedBaseId)?.Name ?? "?"}은(는) " +
              $"{string.Join("·", slot.BlockedBy)}(같은 편성)가 사용 중"
            : "";
        // A better piece the player has but this limit break cannot equip explains a lower pick first.
        var lockedText = slot.LockedBaseId > 0
            ? $"{slot.LockedRank}순위 {items.GetValueOrDefault(slot.LockedBaseId)?.Name ?? "?"}은(는) " +
              (slot.LockedUntilLimitBreak > 0 ? $"{slot.LockedUntilLimitBreak}돌파부터 장착 가능"
                  : slot.LockedUntilRetrofit ? "개조 후 장착 가능" : "이 칸에 장착 불가")
            : "";
        var reasonText = lockedText.Length > 0 ? lockedText : blockedText;
        string actionKind, actionText, toolTip = "", sub, alt = "";
        switch (slot.Action)
        {
            case GearSlotAction.Move:
                var holder = slot.MoveFrom!;
                actionKind = holder.InSortieFleet ? "move-sortie" : "move";
                actionText = $"{holder.ShipName}에서 옮기기";
                toolTip = holder.InSortieFleet
                    ? $"{holder.ShipName}은(는) 출격 함대에 있습니다. 옮기면 그 함대 장비가 비어요."
                    : $"{holder.ShipName} {(special is null ? $"{holder.Slot}번 칸" : "특수장비")} · +{holder.Enhance}";
                sub = WithReason(nowText, reasonText);
                alt = slot.BagAlternative switch
                {
                    { Action: GearSlotAction.Bag } bag =>
                        $"창고 대체 · {bag.Rank}순위 {items.GetValueOrDefault(bag.BaseId)?.Name ?? "?"} (창고 {SpareCount(spare, bag.BaseId)}개)",
                    { Action: GearSlotAction.Keep } => "창고만 쓰면 지금 장비 유지",
                    _ => "창고 대체 없음"
                };
                break;
            case GearSlotAction.Bag:
                actionKind = "bag";
                actionText = $"창고 {SpareCount(spare, slot.BaseId)}개";
                sub = WithReason(nowText, reasonText);
                break;
            case GearSlotAction.Keep when slot.Rank > 0:
                actionKind = "keep";
                actionText = "✓ 유지";
                sub = reasonText.Length > 0 ? reasonText : slot.Rank == 1 ? "1순위 장착 중" : "더 좋은 장비를 구할 곳 없음";
                break;
            case GearSlotAction.Keep:
                actionKind = "hold";
                actionText = "유지";
                sub = reasonText.Length > 0 ? reasonText
                    : mode == GearSourceMode.Bag ? "창고에 순위 안 장비 없음" : "순위 안 장비를 구할 곳 없음";
                break;
            case GearSlotAction.Missing:
                actionKind = "missing";
                actionText = "미보유";
                sub = reasonText.Length > 0 ? $"빈 칸 · {reasonText}" : "빈 칸 · 순위 안 장비를 구할 곳 없음";
                break;
            default:
                actionKind = "nodata";
                actionText = "";
                sub = lockedText.Length > 0 ? lockedText : noDataText;
                break;
        }

        if (differs && alternateArmorLabel.Length > 0)
            sub += $" · {alternateArmorLabel} 순위 다름";
        var enhance = slot.Action is GearSlotAction.Keep or GearSlotAction.Move ? slot.Enhance : 0;
        return new GearSlotRow
        {
            Slot = slot.Slot,
            SlotText = slotText,
            SlotLabel = special?.Label ?? (shown ?? current)?.TypeLabel ?? "",
            IconUrl = shown?.IconUrl ?? "",
            Rarity = shown?.Rarity ?? "",
            RankText = RankLabel(slot.Rank),
            IsOutOfRank = slot.Rank == 0,
            Name = shown is null
                ? "장비 없음"
                : enhance > 0 ? string.Create(CultureInfo.InvariantCulture, $"{shown.Name} +{enhance}") : shown.Name,
            ActionKind = actionKind,
            ActionText = actionText,
            ActionToolTip = toolTip.Length > 0 ? toolTip : special?.ToolTip ?? "",
            SubText = sub,
            AltText = alt,
            DiffersInAlternateArmor = differs
        };
    }

    private static string WithReason(string text, string reason) => reason.Length > 0 ? $"{text} · {reason}" : text;

    private static string RankLabel(int rank) => rank > 0 ? $"{rank}순위" : "순위 밖";

    private static int SpareCount(IReadOnlyDictionary<int, IReadOnlyList<int>> spare, int baseId) =>
        spare.TryGetValue(baseId, out var copies) ? copies.Count : 0;
}
