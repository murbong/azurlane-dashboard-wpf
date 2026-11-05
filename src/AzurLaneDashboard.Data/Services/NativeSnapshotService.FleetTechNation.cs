using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private sealed record FleetTechNationState(
        IReadOnlyCollection<int> CompleteTechIds,
        IReadOnlyDictionary<(int ShipType, int AttrType), int> SetValues)
    {
        public static FleetTechNationState Empty { get; } =
            new([], new Dictionary<(int ShipType, int AttrType), int>());
    }

    private static void ApplyFleetTechNationReloadAdditions(
        SqliteConnection connection,
        List<ShipState> ships,
        JsonElement fleetTechNationMap,
        List<PacketRecord>? packets = null)
    {
        if (ships.Count == 0 || fleetTechNationMap.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var state = ReadFleetTechNationState(connection, packets);
        if (state.CompleteTechIds.Count == 0 && state.SetValues.Count == 0)
        {
            return;
        }

        var additions = BuildFleetTechNationAdditions(fleetTechNationMap, state);
        foreach (var ship in ships)
        {
            if (ship.Reload <= 0 ||
                ship.ShipTypeId <= 0 ||
                !additions.TryGetValue((ship.ShipTypeId, ReloadAttributeTypeId), out var addition) ||
                addition <= 0)
            {
                continue;
            }

            ship.Reload += addition;
            ship.StatsDetail = ReplaceStatLine(ship.StatsDetail, "장전", ship.Reload);
        }
    }

    private static FleetTechNationState ReadFleetTechNationState(
        SqliteConnection connection,
        List<PacketRecord>? packets)
    {
        var current = ReadCurrentFleetTechNationState(connection);
        if (current.CompleteTechIds.Count > 0 || current.SetValues.Count > 0)
        {
            return current;
        }

        return packets is null ? FleetTechNationState.Empty : ReadPacketFleetTechNationState(packets);
    }

    private static FleetTechNationState ReadCurrentFleetTechNationState(SqliteConnection connection)
    {
        if (!TableExists(connection, "current_fleet_tech_groups"))
        {
            return FleetTechNationState.Empty;
        }

        var completeTechIds = new List<int>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT effect_tech_id FROM current_fleet_tech_groups WHERE effect_tech_id > 0";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var techId = Int(reader, "effect_tech_id");
                if (techId > 0)
                {
                    completeTechIds.Add(techId);
                }
            }
        }

        var setValues = new Dictionary<(int ShipType, int AttrType), int>();
        if (TableExists(connection, "current_fleet_tech_attr_settings"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT ship_type, attr_type, set_value FROM current_fleet_tech_attr_settings";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var shipType = Int(reader, "ship_type");
                var attrType = Int(reader, "attr_type");
                if (shipType > 0 && attrType > 0)
                {
                    setValues[(shipType, attrType)] = Int(reader, "set_value");
                }
            }
        }

        return new FleetTechNationState(completeTechIds, setValues);
    }

    private static FleetTechNationState ReadPacketFleetTechNationState(List<PacketRecord> packets)
    {
        var snapshot = packets
            .Where(packet => packet.PacketId == 64000 && packet.Direction == "SC")
            .OrderByDescending(packet => packet.RowId)
            .FirstOrDefault();
        if (snapshot is null)
        {
            return FleetTechNationState.Empty;
        }

        var groups = new Dictionary<int, (int EffectTechId, int StudyTechId)>();
        foreach (var tech in Array(snapshot.Json, "tech_list"))
        {
            var groupId = JsonInt(tech, "group_id");
            if (groupId > 0)
            {
                groups[groupId] = (JsonInt(tech, "effect_tech_id"), JsonInt(tech, "study_tech_id"));
            }
        }

        var setValues = ReadFleetTechSetValues(Array(snapshot.Json, "techset_list"));
        var pendingStart = (GroupId: 0, TechId: 0);
        var pendingFinishGroupId = 0;
        IReadOnlyDictionary<(int ShipType, int AttrType), int>? pendingSetValues = null;

        foreach (var packet in packets
                     .Where(packet => packet.RowId > snapshot.RowId && packet.PacketId is >= 64001 and <= 64010)
                     .OrderBy(packet => packet.RowId))
        {
            switch (packet.PacketId)
            {
                case 64001 when packet.Direction == "CS":
                    pendingStart = (JsonInt(packet.Json, "tech_group_id"), JsonInt(packet.Json, "tech_id"));
                    break;
                case 64002 when packet.Direction == "SC" && JsonInt(packet.Json, "result") == 0:
                    if (pendingStart.GroupId > 0 && pendingStart.TechId > 0)
                    {
                        var startGroup = groups.GetValueOrDefault(pendingStart.GroupId);
                        groups[pendingStart.GroupId] = (startGroup.EffectTechId, pendingStart.TechId);
                    }

                    pendingStart = (0, 0);
                    break;
                case 64003 when packet.Direction == "CS":
                    pendingFinishGroupId = JsonInt(packet.Json, "tech_group_id");
                    break;
                case 64004 when packet.Direction == "SC" && JsonInt(packet.Json, "result") == 0:
                    if (pendingFinishGroupId > 0 &&
                        groups.TryGetValue(pendingFinishGroupId, out var finishGroup) &&
                        finishGroup.StudyTechId > 0)
                    {
                        groups[pendingFinishGroupId] = (finishGroup.StudyTechId, 0);
                    }

                    pendingFinishGroupId = 0;
                    break;
                case 64009 when packet.Direction == "CS":
                    pendingSetValues = ReadFleetTechSetValues(Array(packet.Json, "techset_list"));
                    break;
                case 64010 when packet.Direction == "SC" && JsonInt(packet.Json, "result") == 0:
                    if (pendingSetValues is not null)
                    {
                        setValues = new Dictionary<(int ShipType, int AttrType), int>(pendingSetValues);
                    }

                    pendingSetValues = null;
                    break;
            }
        }

        return new FleetTechNationState(
            groups.Values.Select(group => group.EffectTechId).Where(techId => techId > 0).Distinct().ToArray(),
            setValues);
    }

    private static Dictionary<(int ShipType, int AttrType), int> ReadFleetTechSetValues(IEnumerable<JsonElement> items)
    {
        var result = new Dictionary<(int ShipType, int AttrType), int>();
        foreach (var item in items)
        {
            var shipType = JsonInt(item, "ship_type");
            var attrType = JsonInt(item, "attr_type");
            if (shipType > 0 && attrType > 0)
            {
                result[(shipType, attrType)] = JsonInt(item, "set_value");
            }
        }

        return result;
    }

    private static Dictionary<(int ShipType, int AttrType), int> BuildFleetTechNationAdditions(
        JsonElement fleetTechNationMap,
        FleetTechNationState state)
    {
        var result = new Dictionary<(int ShipType, int AttrType), int>();
        foreach (var techId in state.CompleteTechIds)
        {
            var tech = MapObject(fleetTechNationMap, techId);
            foreach (var addition in Array(tech, "add"))
            {
                var attrType = JsonInt(addition, "attr_type");
                var value = JsonInt(addition, "value");
                if (attrType <= 0 || value == 0)
                {
                    continue;
                }

                foreach (var shipType in Array(addition, "ship_types").Select(IntValue).Where(item => item > 0))
                {
                    var key = (shipType, attrType);
                    result[key] = result.GetValueOrDefault(key) + value;
                }
            }
        }

        foreach (var (key, value) in state.SetValues)
        {
            result[key] = value;
        }

        return result;
    }
}
