using System.Globalization;

namespace AzurLaneDashboard.Core.Models;

public sealed class FleetCompositionGroupRow
{
    public int FleetId { get; init; }
    public string FleetName { get; init; } = "";
    public string FleetKind { get; init; } = "";
    public string CommanderSummary { get; init; } = "";
    public IReadOnlyList<FleetCompositionShipRow> Ships { get; init; } = [];
    public IReadOnlyList<FleetDamageRow> TimingRows { get; init; } = [];

    public int ShipCount => Ships.Count;
    public int TimingCount => TimingRows.Count;
    public string Header => string.IsNullOrWhiteSpace(FleetKind) ? FleetName : $"{FleetName} · {FleetKind}";
    public string FleetIcon => FleetId is >= 11 and <= 12 ? "◈" : "▦";
    public IReadOnlyList<FleetCompositionFormationRow> FormationRows => BuildFormationRows();

    public string Summary => string.Join(" · ",
        new[]
        {
            $"{ShipCount.ToString("N0", CultureInfo.InvariantCulture)}척",
            $"{TimingCount.ToString("N0", CultureInfo.InvariantCulture)}개 타이밍",
            CommanderCount > 0 ? $"지휘냥 {CommanderCount.ToString("N0", CultureInfo.InvariantCulture)}" : ""
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    // The stored summary is "slot:commanderId, ..."; the raw IDs mean nothing to a player.
    private int CommanderCount => CommanderSummary
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    private IReadOnlyList<FleetCompositionFormationRow> BuildFormationRows()
    {
        var placed = Ships
            .Where(ship => ship.FleetSlot > 0)
            .GroupBy(ship => ship.FleetSlot)
            .Select(group => group.First())
            .OrderBy(ship => ship.FleetSlot)
            .ToList();

        if (FleetId is >= 11 and <= 12)
            return [BuildFormationRow("잠수", placed)];

        // The hull decides the row, not the slot: a fleet with two vanguards stores a main ship in slot 3.
        return
        [
            BuildFormationRow("후열", placed.Where(ship => FormationOf(ship) == "후열").ToList()),
            BuildFormationRow("전열", placed.Where(ship => FormationOf(ship) == "전열").ToList())
        ];
    }

    // Ships missing from the dock data have no hull, so only they fall back to the slot number.
    private static string FormationOf(FleetCompositionShipRow ship) =>
        ship.FormationGroup is "전열" or "후열" ? ship.FormationGroup : ship.FleetSlot <= 3 ? "전열" : "후열";

    private static FleetCompositionFormationRow BuildFormationRow(
        string formationName, IReadOnlyList<FleetCompositionShipRow> ships)
    {
        return new FleetCompositionFormationRow
        {
            FormationName = formationName,
            Slots = Enumerable.Range(0, Math.Max(3, ships.Count))
                .Select(index => new FleetCompositionSlotRow
                {
                    FormationName = formationName,
                    SlotIndex = index < ships.Count ? ships[index].FleetSlot : 0,
                    Ship = index < ships.Count ? ships[index] : null
                })
                .ToArray()
        };
    }
}

public sealed class FleetCompositionFormationRow
{
    public string FormationName { get; init; } = "";
    public IReadOnlyList<FleetCompositionSlotRow> Slots { get; init; } = [];
}

public sealed class FleetCompositionSlotRow
{
    public string FormationName { get; init; } = "";
    public int SlotIndex { get; init; }
    public FleetCompositionShipRow? Ship { get; init; }

    public bool HasShip => Ship is not null;
    public bool IsEmpty => Ship is null;
    public string SlotText => SlotIndex > 0 ? $"#{SlotIndex.ToString("N0", CultureInfo.InvariantCulture)}" : "-";
    public string EmptyText => SlotIndex > 0 ? $"{FormationName} {SlotText}" : $"{FormationName} 빈 자리";
    public string ShipName => Ship?.ShipName ?? "";
    public string ShipIconUrl => Ship?.ShipIconUrl ?? "";
    public string ShipSummary => Ship?.ShipDisplaySummary ?? "";
    public string ShipTimingText => Ship?.TimingText ?? "";
    public string Rarity => Ship?.Rarity ?? "";
    public bool IsProposed => Ship?.IsProposed == true;
}

public sealed class FleetCompositionShipRow
{
    public int FleetId { get; init; }
    public string FleetName { get; init; } = "";
    public int FleetSlot { get; init; }
    public string FormationGroup { get; init; } = "";
    public string ShipUid { get; init; } = "";
    public string ShipName { get; init; } = "";
    public string ShipIconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public int ShipLevel { get; init; }
    public int ShipReload { get; init; }
    public string ShipTypeLabel { get; init; } = "";
    public string NationLabel { get; init; } = "";
    public bool IsProposed { get; init; }
    public string EquippedSummary { get; init; } = "";
    public string TimingSummary { get; init; } = "";
    public string TimingStatus { get; init; } = "";

    public string SlotText => FleetSlot > 0 ? $"#{FleetSlot.ToString("N0", CultureInfo.InvariantCulture)}" : "-";
    public string ShipDisplayName => ShipName;
    public string ShipDisplayIconUrl => ShipIconUrl;

    public string ShipDisplaySummary => string.Join(" · ",
        new[] { LevelText, ShipTypeLabel, NationLabel }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string LevelText => ShipLevel > 0 ? $"Lv.{ShipLevel.ToString("N0", CultureInfo.InvariantCulture)}" : "";
    public string ReloadText => ShipReload > 0 ? ShipReload.ToString("N0", CultureInfo.InvariantCulture) : "-";
    public string TimingText => string.IsNullOrWhiteSpace(TimingSummary) ? TimingStatus : TimingSummary;
}
