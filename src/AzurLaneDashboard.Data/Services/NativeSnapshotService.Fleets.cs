using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static FleetLayout ReadCurrentFleetLayout(SqliteConnection connection)
    {
        if (!TableExists(connection, "current_fleets") || !TableExists(connection, "current_fleet_ships"))
        {
            return FleetLayout.Empty;
        }

        var headers = ReadFleetHeaders(connection);
        var groupedAssignments = new Dictionary<int, List<FleetAssignment>>();
        var assignments = new Dictionary<string, FleetAssignment>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT s.ship_uid,
                                     s.fleet_id,
                                     COALESCE(f.name, '') AS name,
                                     s.slot_index,
                                     COALESCE(f.commander_summary, '') AS commander_summary
                              FROM current_fleet_ships s
                              LEFT JOIN current_fleets f ON f.fleet_id = s.fleet_id
                              ORDER BY s.fleet_id, s.slot_index
                              """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var uid = Text(reader, "ship_uid");
            if (string.IsNullOrWhiteSpace(uid))
            {
                continue;
            }

            var assignment = new FleetAssignment(
                uid,
                Int(reader, "fleet_id"),
                Text(reader, "name"),
                Int(reader, "slot_index"),
                Text(reader, "commander_summary"));

            if (!groupedAssignments.TryGetValue(assignment.FleetId, out var groupShips))
            {
                groupShips = [];
                groupedAssignments[assignment.FleetId] = groupShips;
            }

            groupShips.Add(assignment);

            if (!assignments.TryGetValue(uid, out var existing) || ShouldReplaceFleetAssignment(existing, assignment))
            {
                assignments[uid] = assignment;
            }
        }

        var groups = headers
            .Select(header => new FleetGroupLayout(
                header.FleetId,
                header.FleetName,
                header.CommanderSummary,
                groupedAssignments.GetValueOrDefault(header.FleetId)?
                    .OrderBy(assignment => assignment.SlotIndex)
                    .ToArray() ?? []))
            .OrderBy(group => FleetSortOrder(group.FleetId))
            .ThenBy(group => group.FleetId)
            .ToArray();

        return new FleetLayout(assignments, groups, assignments.Count > 0 || groups.Length > 0);
    }

    private static FleetLayout BuildFleetLayoutFromPackets(IReadOnlyList<PacketRecord> packets)
    {
        var groups = new Dictionary<int, MutableFleetGroup>();
        var pendingCommits = new Dictionary<string, PacketFleetCommit>(StringComparer.Ordinal);
        var pendingRenames = new Dictionary<string, PacketFleetRename>(StringComparer.Ordinal);

        foreach (var packet in packets.OrderBy(packet => packet.RowId).ThenBy(packet => packet.Index))
        {
            if (packet.Direction == "SC" && packet.PacketId == 12101)
            {
                groups.Clear();
                foreach (var group in Array(packet.Json, "group_list"))
                {
                    ApplyPacketFleetGroup(groups, group);
                }
            }
            else if (packet.Direction == "SC" && packet.PacketId == 12106)
            {
                if (packet.Json.ValueKind == JsonValueKind.Object &&
                    packet.Json.TryGetProperty("group", out var group) &&
                    group.ValueKind == JsonValueKind.Object)
                {
                    ApplyPacketFleetGroup(groups, group);
                }
            }
            else if (packet.Direction == "CS" && packet.PacketId == 12102)
            {
                var commit = ReadPacketFleetCommit(packet.Json);
                if (commit is not null)
                {
                    pendingCommits[PacketFleetKey(packet)] = commit;
                }
            }
            else if (packet.Direction == "SC" && packet.PacketId == 12103)
            {
                var key = PacketFleetKey(packet);
                if (JsonInt(packet.Json, "result") == 0 &&
                    pendingCommits.TryGetValue(key, out var commit))
                {
                    ApplyPacketFleetCommit(groups, commit);
                }

                pendingCommits.Remove(key);
            }
            else if (packet.Direction == "CS" && packet.PacketId == 12104)
            {
                var rename = ReadPacketFleetRename(packet.Json);
                if (rename is not null)
                {
                    pendingRenames[PacketFleetKey(packet)] = rename;
                }
            }
            else if (packet.Direction == "SC" && packet.PacketId == 12105)
            {
                var key = PacketFleetKey(packet);
                if (JsonInt(packet.Json, "result") == 0 &&
                    pendingRenames.TryGetValue(key, out var rename))
                {
                    ApplyPacketFleetRename(groups, rename);
                }

                pendingRenames.Remove(key);
            }
        }

        return BuildFleetLayout(groups.Values);
    }

    private static void ApplyPacketFleetGroup(Dictionary<int, MutableFleetGroup> groups, JsonElement groupJson)
    {
        var fleetId = JsonInt(groupJson, "id");
        if (fleetId <= 0)
        {
            return;
        }

        var group = EnsurePacketFleetGroup(groups, fleetId);
        var name = JsonString(groupJson, "name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            group.FleetName = name;
        }

        group.CommanderSummary = PacketFleetCommanderSummary(Array(groupJson, "commanders"));
        group.ShipUids = PacketFleetShipIds(groupJson).ToList();
    }

    private static PacketFleetCommit? ReadPacketFleetCommit(JsonElement json)
    {
        var fleetId = JsonInt(json, "id");
        return fleetId > 0
            ? new PacketFleetCommit(fleetId, PacketFleetShipIds(json))
            : null;
    }

    private static PacketFleetRename? ReadPacketFleetRename(JsonElement json)
    {
        var fleetId = JsonInt(json, "id");
        return fleetId > 0
            ? new PacketFleetRename(fleetId, JsonString(json, "name"))
            : null;
    }

    private static void ApplyPacketFleetCommit(Dictionary<int, MutableFleetGroup> groups, PacketFleetCommit commit)
    {
        EnsurePacketFleetGroup(groups, commit.FleetId).ShipUids = commit.ShipUids.ToList();
    }

    private static void ApplyPacketFleetRename(Dictionary<int, MutableFleetGroup> groups, PacketFleetRename rename)
    {
        EnsurePacketFleetGroup(groups, rename.FleetId).FleetName = rename.FleetName;
    }

    private static MutableFleetGroup EnsurePacketFleetGroup(Dictionary<int, MutableFleetGroup> groups, int fleetId)
    {
        if (groups.TryGetValue(fleetId, out var group))
        {
            return group;
        }

        group = new MutableFleetGroup(fleetId);
        groups[fleetId] = group;
        return group;
    }

    private static FleetLayout BuildFleetLayout(IEnumerable<MutableFleetGroup> sourceGroups)
    {
        var assignments = new Dictionary<string, FleetAssignment>(StringComparer.Ordinal);
        var groups = sourceGroups
            .OrderBy(group => FleetSortOrder(group.FleetId))
            .ThenBy(group => group.FleetId)
            .Select(group =>
            {
                var slot = 1;
                var groupAssignments = group.ShipUids
                    .Where(uid => !string.IsNullOrWhiteSpace(uid) && uid != "0")
                    .Select(uid => new FleetAssignment(
                        uid,
                        group.FleetId,
                        group.FleetName,
                        slot++,
                        group.CommanderSummary))
                    .ToArray();

                foreach (var assignment in groupAssignments)
                {
                    if (!assignments.TryGetValue(assignment.ShipUid, out var existing) ||
                        ShouldReplaceFleetAssignment(existing, assignment))
                    {
                        assignments[assignment.ShipUid] = assignment;
                    }
                }

                return new FleetGroupLayout(
                    group.FleetId,
                    group.FleetName,
                    group.CommanderSummary,
                    groupAssignments);
            })
            .ToArray();

        return new FleetLayout(assignments, groups, assignments.Count > 0 || groups.Length > 0);
    }

    private static IReadOnlyList<string> PacketFleetShipIds(JsonElement json)
    {
        return Array(json, "ship_list")
            .Select(ValueText)
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "0")
            .ToArray();
    }

    private static string PacketFleetCommanderSummary(IEnumerable<JsonElement> commanders)
    {
        return string.Join(", ", commanders
            .Select(commander =>
            {
                var pos = JsonInt(commander, "pos");
                var id = JsonInt(commander, "id");
                return pos > 0 && id > 0 ? $"{pos}:{id}" : "";
            })
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string PacketFleetKey(PacketRecord packet) => packet.Source;

    private static List<FleetGroupHeader> ReadFleetHeaders(SqliteConnection connection)
    {
        var headers = new List<FleetGroupHeader>();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT fleet_id,
                                     COALESCE(name, '') AS name,
                                     COALESCE(commander_summary, '') AS commander_summary
                              FROM current_fleets
                              ORDER BY fleet_id
                              """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            headers.Add(new FleetGroupHeader(
                Int(reader, "fleet_id"),
                Text(reader, "name"),
                Text(reader, "commander_summary")));
        }

        return headers;
    }

    // Only the fleets shown on the fleet composition page count as sortie fleets for ship status.
    private static void ApplyFleetLabels(IEnumerable<ShipState> ships, FleetLayout fleetLayout)
    {
        foreach (var ship in ships)
        {
            var inFleet = fleetLayout.Assignments.TryGetValue(ship.Uid, out var fleet) && IsDashboardFleet(fleet.FleetId);
            ship.FleetId = inFleet ? fleet!.FleetId : 0;
            ship.FleetLabel = inFleet ? fleet!.ShortName : "";
        }
    }

    private static bool ShouldReplaceFleetAssignment(FleetAssignment existing, FleetAssignment candidate)
    {
        var existingIsRegular = IsRegularFleet(existing.FleetId);
        var candidateIsRegular = IsRegularFleet(candidate.FleetId);
        if (existingIsRegular != candidateIsRegular)
        {
            return candidateIsRegular;
        }

        if (existing.FleetId != candidate.FleetId)
        {
            return candidate.FleetId < existing.FleetId;
        }

        return candidate.SlotIndex > 0 &&
               (existing.SlotIndex <= 0 || candidate.SlotIndex < existing.SlotIndex);
    }

    private static bool IsRegularFleet(int fleetId)
    {
        return fleetId is >= 1 and <= 6;
    }

    private static bool IsSubmarineFleet(int fleetId)
    {
        return fleetId is >= 11 and <= 12;
    }

    private static bool IsDashboardFleet(int fleetId)
    {
        return IsRegularFleet(fleetId) || IsSubmarineFleet(fleetId);
    }

    private static int FleetSortOrder(int fleetId)
    {
        return IsRegularFleet(fleetId) ? fleetId :
            IsSubmarineFleet(fleetId) ? 100 + fleetId :
            1000 + fleetId;
    }

    private sealed record FleetGroupHeader(int FleetId, string FleetName, string CommanderSummary);

    private sealed class MutableFleetGroup(int fleetId)
    {
        public int FleetId { get; } = fleetId;

        public string FleetName { get; set; } = "";

        public string CommanderSummary { get; set; } = "";

        public List<string> ShipUids { get; set; } = [];
    }

    private sealed record PacketFleetCommit(int FleetId, IReadOnlyList<string> ShipUids);

    private sealed record PacketFleetRename(int FleetId, string FleetName);
}
