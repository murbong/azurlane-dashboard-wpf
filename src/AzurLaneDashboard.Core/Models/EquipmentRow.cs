namespace AzurLaneDashboard.Core.Models;

public sealed class EquipmentRow
{
    private string? equippedShipsToolTip;
    private string? searchText;
    private string? toolTipText;
    private IReadOnlyList<EquipmentEquippedShipRow>? visibleEquippedShips;

    public string TemplateId { get; init; } = "";
    public string Name { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string RarityTier { get; init; } = "";
    public string RarityClass { get; init; } = "";
    public string EquipmentTypeGroup { get; init; } = "";

    public string EquipmentTypeLabel => TypeLabel(EquipmentTypeGroup);

    public static string TypeLabel(string typeGroup) => typeGroup switch
    {
        "gun" => "함포",
        "aircraft" => "항공",
        "torpedo" => "어뢰",
        "antiair" => "대공",
        "auxiliary" => "설비",
        "asw" => "대잠",
        "missile" => "미사일",
        _ => "기타"
    };

    public string EquipmentTypeIcon => EquipmentTypeGroup switch
    {
        "gun" => "⌖",
        "aircraft" => "✈",
        "torpedo" => "⇢",
        "antiair" => "✹",
        "auxiliary" => "◆",
        "asw" => "◌",
        "missile" => "↟",
        _ => "•"
    };

    public int EnhanceLevel { get; init; }
    public int Count { get; init; }
    public int BagCount { get; init; }
    public int EquippedCount { get; init; }
    public int Score { get; init; }
    public string Action { get; init; } = "";
    public IReadOnlyList<EquipmentAttributeRow> Attributes { get; init; } = [];
    public string AttributeSummary => EquipmentAttributeRow.Join(Attributes);
    public string EquippedBySummary { get; init; } = "";
    public IReadOnlyList<EquipmentEquippedShipRow> EquippedShips { get; init; } = [];
    public string EquippedByText => string.IsNullOrWhiteSpace(EquippedBySummary) ? "미장착" : EquippedBySummary;
    public IReadOnlyList<EquipmentEquippedShipRow> VisibleEquippedShips =>
        visibleEquippedShips ??= EquippedShips.Take(3).ToArray();

    public int HiddenEquippedShipCount => Math.Max(0, EquippedShips.Count - 3);
    public bool HasHiddenEquippedShips => HiddenEquippedShipCount > 0;
    public string HiddenEquippedShipLabel => HiddenEquippedShipCount > 0 ? $"+{HiddenEquippedShipCount:N0}" : "";

    public string EquippedShipsToolTip => equippedShipsToolTip ??= string.Join(", ",
        EquippedShips.Select(ship => $"{ship.ShipName} #{ship.Slot:N0}")
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    public string ToolTipText => toolTipText ??= string.Join(Environment.NewLine,
        new[]
        {
            Name,
            $"{Rarity} · {EquipmentTypeLabel} · +{EnhanceLevel:N0}",
            AttributeSummary,
            $"보유 {Count:N0} · 창고 {BagCount:N0} · 장착 {EquippedCount:N0}",
            string.IsNullOrWhiteSpace(TemplateId) ? "" : $"ID {TemplateId}",
            EquippedBySummary
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string SearchText => searchText ??=
        $"{TemplateId} {Name} {Rarity} {RarityTier} {EquipmentTypeGroup} {EquipmentTypeLabel} +{EnhanceLevel} {AttributeSummary} {EquippedBySummary}"
            .ToLowerInvariant();
}

public sealed record EquipmentAttributeRow(int Index, string Type, string Label, int Value)
{
    public string ValueText => Value > 0 ? $"+{Value:N0}" : $"{Value:N0}";

    public string DisplayText => string.IsNullOrWhiteSpace(Label)
        ? $"{Type} {ValueText}"
        : $"{Label} {ValueText}";

    public static string Join(IEnumerable<EquipmentAttributeRow> attributes) =>
        string.Join(" · ", attributes
            .OrderBy(attribute => attribute.Index)
            .Select(attribute => attribute.DisplayText)
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    public static string LabelFor(string type) => type switch
    {
        "durability" => "내구",
        "cannon" => "화력",
        "torpedo" => "뇌장",
        "antiaircraft" => "대공",
        "air" => "항공",
        "reload" => "장전",
        "hit" => "명중",
        "dodge" => "회피",
        "speed" => "속도",
        "luck" => "행운",
        "antisub" => "대잠",
        "armor" => "장갑",
        _ => type
    };
}

public sealed record EquipmentEquippedShipRow(
    string ShipUid,
    string ShipName,
    string ShipIconUrl,
    string RarityClass,
    int Slot);
