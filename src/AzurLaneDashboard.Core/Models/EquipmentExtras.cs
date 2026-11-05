namespace AzurLaneDashboard.Core.Models;

public sealed class SpecialEquipmentRow
{
    private string? equippedShipsToolTip;
    private string? searchText;
    private IReadOnlyList<EquipmentEquippedShipRow>? visibleEquippedShips;

    public string Id { get; init; } = "";
    public string TemplateId { get; init; } = "";
    public string Name { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string RarityTier { get; init; } = "";
    public string RarityClass { get; init; } = "";
    public int EnhanceLevel { get; init; }
    public string Effect { get; init; } = "";
    public int Pt { get; init; }
    public string EquippedBySummary { get; init; } = "";
    public IReadOnlyList<EquipmentEquippedShipRow> EquippedShips { get; init; } = [];
    public int EquippedCount => EquippedShips.Count;
    public string EquippedByText => string.IsNullOrWhiteSpace(EquippedBySummary) ? "미장착" : EquippedBySummary;
    public IReadOnlyList<EquipmentEquippedShipRow> VisibleEquippedShips =>
        visibleEquippedShips ??= EquippedShips.Take(3).ToArray();
    public int HiddenEquippedShipCount => Math.Max(0, EquippedShips.Count - 3);
    public bool HasHiddenEquippedShips => HiddenEquippedShipCount > 0;
    public string HiddenEquippedShipLabel => HiddenEquippedShipCount > 0 ? $"+{HiddenEquippedShipCount:N0}" : "";

    public string EquippedShipsToolTip => equippedShipsToolTip ??= string.Join(", ",
        EquippedShips.Select(ship => ship.ShipName)
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    public string ToolTipText => string.Join(Environment.NewLine,
        new[]
        {
            Name,
            $"{Rarity} · +{EnhanceLevel:N0}",
            string.IsNullOrWhiteSpace(Effect) ? "" : $"효과 {Effect}",
            Pt > 0 ? $"PT {Pt:N0}" : "",
            EquippedCount > 0 ? $"장착 {EquippedBySummary}" : "미장착",
            string.IsNullOrWhiteSpace(TemplateId) ? "" : $"ID {TemplateId}"
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string SearchText => searchText ??= $"{Id} {TemplateId} {Name} {Rarity} {RarityTier} {Effect} {EquippedBySummary}"
        .ToLowerInvariant();
}

public sealed class EquipmentSkinRow
{
    private string? searchText;

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string RarityTier { get; init; } = "";
    public string RarityClass { get; init; } = "";
    public int Count { get; init; }
    public string ThemeId { get; init; } = "";

    public string ToolTipText => string.Join(Environment.NewLine,
        new[]
        {
            Name,
            string.IsNullOrWhiteSpace(Rarity) ? "" : $"등급 {Rarity}",
            $"보유 {Count:N0}",
            string.IsNullOrWhiteSpace(ThemeId) ? "" : $"테마 {ThemeId}",
            string.IsNullOrWhiteSpace(Id) ? "" : $"ID {Id}"
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string SearchText => searchText ??= $"{Id} {Name} {Rarity} {RarityTier} {ThemeId}"
        .ToLowerInvariant();
}

public sealed class GearGapRow
{
    private IReadOnlyList<ShipEquipmentSlotRow>? loadoutSlots;
    private string? shipMetaSummary;
    private string? searchText;

    public string ShipUid { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string RarityTier { get; init; } = "";
    public string RarityClass { get; init; } = "";
    public int ShipTypeId { get; init; }
    public string ShipTypeLabel { get; init; } = "";
    public string ShipTypeGroup { get; init; } = "";
    public int NationId { get; init; }
    public string NationLabel { get; init; } = "";
    public bool IsProposed { get; init; }
    public string ShipDisplayName => DisplayName;
    public string ShipDisplayIconUrl => IconUrl;
    public string ShipDisplaySummary => ShipMetaSummary;

    // The fleet leads the subtitle so the sortie ships at the top of the list read as one group.
    public string ShipMetaSummary => shipMetaSummary ??= string.Join(" · ",
        new[] { FleetLabel, ShipTypeLabel, NationLabel }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public int Level { get; init; }
    public int EmptySlots { get; init; }
    public string FleetLabel { get; init; } = "";
    public int FleetOrder { get; init; }
    public string EquippedSummary { get; init; } = "";
    public string SpecialEquipmentSummary { get; init; } = "";
    public IReadOnlyList<ShipEquipmentSlotRow> EquipmentSlots { get; init; } = [];
    public ShipEquipmentSlotRow? SpecialEquipmentSlot { get; init; }

    public ShipEquipmentSlotRow DisplaySpecialEquipmentSlot =>
        SpecialEquipmentSlot ?? ShipEquipmentSlotRow.EmptySpecialEquipmentSlot;

    public IReadOnlyList<ShipEquipmentSlotRow> LoadoutSlots =>
        loadoutSlots ??= EquipmentSlots.Concat([DisplaySpecialEquipmentSlot]).ToArray();

    public string SearchText => searchText ??=
        $"{ShipUid} {DisplayName} {Rarity} {RarityTier} {FleetLabel} {ShipTypeLabel} {NationLabel} {EquippedSummary} {SpecialEquipmentSummary}"
            .ToLowerInvariant();
}

public sealed class CommunityGearShortageRow
{
    private string? searchText;

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string RarityTier { get; init; } = "";
    public string RarityClass { get; init; } = "";
    public int MissingForCount { get; init; }
    public string TopShipNames { get; init; } = "";
    public int Score { get; init; }

    public string ToolTipText => string.Join(Environment.NewLine,
        new[]
        {
            Name,
            string.IsNullOrWhiteSpace(Rarity) ? "" : $"등급 {Rarity}",
            $"부족 대상 {MissingForCount:N0}",
            TopShipNames,
            string.IsNullOrWhiteSpace(Id) ? "" : $"ID {Id}"
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string SearchText => searchText ??= $"{Id} {Name} {Rarity} {RarityTier} {TopShipNames}"
        .ToLowerInvariant();
}

public sealed class CleanupCandidateRow
{
    public string TemplateId { get; init; } = "";
    public string Name { get; init; } = "";
    public int Count { get; init; }
    public string Reason { get; init; } = "";
}
