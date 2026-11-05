using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static List<FleetCompositionGroupRow> BuildFleetCompositionGroups(
        IEnumerable<ShipState> ships,
        FleetLayout fleetLayout,
        IReadOnlyList<FleetDamageRow> timingRows,
        JsonElement fireRateEquipmentMap,
        JsonElement equipmentMap)
    {
        if (!fleetLayout.HasFleetData)
        {
            return BuildFallbackFleetCompositionGroups(timingRows);
        }

        var shipsByUid = ships.ToDictionary(ship => ship.Uid, StringComparer.Ordinal);
        var timingsByShip = timingRows
            .GroupBy(row => row.ShipUid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var groups = fleetLayout.Groups
            .Where(group => IsDashboardFleet(group.FleetId))
            .Select(group => BuildFleetCompositionGroup(group, shipsByUid, timingsByShip, fireRateEquipmentMap, equipmentMap))
            .ToList();

        return groups.Count > 0 ? groups : BuildFallbackFleetCompositionGroups(timingRows);
    }

    private static FleetCompositionGroupRow BuildFleetCompositionGroup(
        FleetGroupLayout group,
        IReadOnlyDictionary<string, ShipState> shipsByUid,
        IReadOnlyDictionary<string, FleetDamageRow[]> timingsByShip,
        JsonElement fireRateEquipmentMap,
        JsonElement equipmentMap)
    {
        var ships = group.Ships
            .Select(assignment => BuildFleetCompositionShip(
                group,
                assignment,
                shipsByUid.GetValueOrDefault(assignment.ShipUid),
                timingsByShip.GetValueOrDefault(assignment.ShipUid) ?? [],
                fireRateEquipmentMap,
                equipmentMap))
            .ToArray();
        var timingRows = group.Ships
            .SelectMany(assignment => timingsByShip.GetValueOrDefault(assignment.ShipUid) ?? [])
            .OrderBy(row => row.FleetSlot > 0 ? row.FleetSlot : 99)
            .ThenBy(row => FormationSort(row.FormationGroup))
            .ThenBy(row => row.Slot > 0 ? row.Slot : 99)
            .ToArray();

        return new FleetCompositionGroupRow
        {
            FleetId = group.FleetId,
            FleetName = group.DisplayName,
            FleetKind = FleetKindLabel(group.FleetId),
            CommanderSummary = group.CommanderSummary,
            Ships = ships,
            TimingRows = timingRows
        };
    }

    private static FleetCompositionShipRow BuildFleetCompositionShip(
        FleetGroupLayout group,
        FleetAssignment assignment,
        ShipState? ship,
        IReadOnlyCollection<FleetDamageRow> timingRows,
        JsonElement fireRateEquipmentMap,
        JsonElement equipmentMap)
    {
        if (ship is null)
        {
            return new FleetCompositionShipRow
            {
                FleetId = group.FleetId,
                FleetName = group.DisplayName,
                FleetSlot = assignment.SlotIndex,
                FormationGroup = IsSubmarineFleet(group.FleetId) ? "잠수" : "",
                ShipUid = assignment.ShipUid,
                ShipName = assignment.ShipUid,
                TimingStatus = "함선 정보 없음"
            };
        }

        var formation = IsSubmarineFleet(group.FleetId) ? "잠수" : FormationLabel(ShipTypeGroup(ship.ShipTypeId));
        return new FleetCompositionShipRow
        {
            FleetId = group.FleetId,
            FleetName = group.DisplayName,
            FleetSlot = assignment.SlotIndex,
            FormationGroup = formation,
            ShipUid = ship.Uid,
            ShipName = ship.DisplayName,
            ShipIconUrl = ship.IconUrl,
            Rarity = ship.Rarity,
            ShipLevel = ship.Level,
            ShipReload = ship.Reload,
            ShipTypeLabel = ShipTypeLabel(ship.ShipTypeId),
            NationLabel = NationLabel(ship.NationId),
            IsProposed = ship.IsProposed,
            EquippedSummary = ship.EquippedSummary,
            TimingSummary = FleetTimingSummary(timingRows),
            TimingStatus = FleetTimingStatus(ship, group.FleetId, timingRows, fireRateEquipmentMap, equipmentMap)
        };
    }

    private static string FleetTimingSummary(IEnumerable<FleetDamageRow> timingRows)
    {
        return string.Join(" · ", timingRows
            .Select(row => row.AttackTimingSummaryText)
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string FleetTimingStatus(
        ShipState ship,
        int fleetId,
        IReadOnlyCollection<FleetDamageRow> timingRows,
        JsonElement fireRateEquipmentMap,
        JsonElement equipmentMap)
    {
        if (timingRows.Count > 0)
        {
            return "계산 가능";
        }

        if (IsSubmarineFleet(fleetId))
        {
            return "잠수함 사속 계산 대기";
        }

        var timedItems = ship.Equipment.Where(IsTimedEquipment).ToArray();
        var airstrikeItems = AirstrikeItems(ship, equipmentMap).ToArray();
        if (timedItems.Length == 0 && airstrikeItems.Length == 0)
        {
            return "계산 대상 장비 없음";
        }

        if (fireRateEquipmentMap.ValueKind != JsonValueKind.Object)
        {
            return "사속 metadata 없음";
        }

        var missingTimedItem = timedItems.FirstOrDefault(item =>
            !TryReadFireRateEquipment(fireRateEquipmentMap, item, out _));
        if (missingTimedItem is not null)
        {
            return $"{AttackRole(missingTimedItem)} metadata 없음";
        }

        return airstrikeItems.Length > 0 ? "공습 metadata 없음" : "계산 불가";
    }

    private static string FleetKindLabel(int fleetId)
    {
        return IsSubmarineFleet(fleetId) ? "잠수" : "일반";
    }

    private static List<FleetCompositionGroupRow> BuildFallbackFleetCompositionGroups(
        IReadOnlyList<FleetDamageRow> timingRows)
    {
        return timingRows
            .GroupBy(row => new { row.FleetId, row.FleetName, row.CommanderSummary })
            .OrderBy(group => FleetSortOrder(group.Key.FleetId))
            .ThenBy(group => group.Key.FleetName, StringComparer.CurrentCulture)
            .Select(group =>
            {
                var rows = group
                    .OrderBy(row => row.FleetSlot > 0 ? row.FleetSlot : 99)
                    .ThenBy(row => FormationSort(row.FormationGroup))
                    .ThenBy(row => row.Slot > 0 ? row.Slot : 99)
                    .ToArray();
                return new FleetCompositionGroupRow
                {
                    FleetId = group.Key.FleetId,
                    FleetName = string.IsNullOrWhiteSpace(group.Key.FleetName) ? "타이밍" : group.Key.FleetName,
                    FleetKind = "타이밍",
                    CommanderSummary = group.Key.CommanderSummary,
                    Ships = rows
                        .GroupBy(row => row.ShipUid, StringComparer.Ordinal)
                        .Select(shipGroup => FallbackCompositionShip(shipGroup.First(), shipGroup.ToArray()))
                        .ToArray(),
                    TimingRows = rows
                };
            })
            .ToList();
    }

    private static FleetCompositionShipRow FallbackCompositionShip(
        FleetDamageRow row,
        IReadOnlyCollection<FleetDamageRow> timingRows)
    {
        return new FleetCompositionShipRow
        {
            FleetId = row.FleetId,
            FleetName = row.FleetName,
            FleetSlot = row.FleetSlot,
            FormationGroup = row.FormationGroup,
            ShipUid = row.ShipUid,
            ShipName = row.ShipName,
            ShipIconUrl = row.ShipIconUrl,
            Rarity = row.ShipRarity,
            ShipLevel = row.ShipLevel,
            ShipReload = row.ShipReload,
            ShipTypeLabel = row.ShipTypeLabel,
            NationLabel = row.NationLabel,
            IsProposed = row.IsProposed,
            EquippedSummary = row.EquipmentSummary,
            TimingSummary = FleetTimingSummary(timingRows),
            TimingStatus = timingRows.Count > 0 ? "계산 가능" : "편성 정보 없음"
        };
    }
}
