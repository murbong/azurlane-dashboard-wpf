using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    // Lua ShipType ids (model/const/shiptype.lua) as hull codes; the fleet check only needs the main-fleet split.
    private static readonly IReadOnlyDictionary<int, string> ShipTypeHulls = new Dictionary<int, string>
    {
        [1] = "DD", [2] = "CL", [3] = "CA", [4] = "BC", [5] = "BB", [6] = "CVL", [7] = "CV", [8] = "SS",
        [10] = "BBV", [12] = "AR", [13] = "BM", [17] = "SSV", [18] = "CB", [19] = "AE", [20] = "DD", [21] = "DD"
    };

    private static readonly int[] SortieFleetIds = [1, 2, 3, 4, 5, 6, 11, 12];

    private static MeowfficerDashboard BuildMeowfficers(SqliteConnection connection, List<ShipState> ships, SnapshotMaps maps,
        int farmFleetId)
    {
        var owned = ReadOwnedMeowfficers(connection);
        if (owned.Count == 0) return MeowfficerDashboard.Empty;
        var catalog = MeowfficerCatalog.Parse(maps.Commanders, maps.MeowfficerGuide);
        owned = MeowfficerFeeding.Apply(catalog, owned, ReadMeowfficerFeeds(connection));
        return MeowfficerPlanBuilder.Build(catalog, owned, ReadMeowfficerFleets(connection, ships), farmFleetId);
    }

    /// <summary>The cats from current_commanders, or from the latest login list before the capture store made that table.</summary>
    private static IReadOnlyList<OwnedMeowfficer> ReadOwnedMeowfficers(SqliteConnection connection)
    {
        var cats = new List<OwnedMeowfficer>();
        if (TableExists(connection, "current_commanders"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT commander_uid, template_id, level, locked, name, abilities_json, ability_origin_json, skills_json, exp
                                  FROM current_commanders
                                  """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var skills = ParseJson(TextOrEmpty(reader, 7));
                cats.Add(new OwnedMeowfficer(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3) == 1,
                    TextOrEmpty(reader, 4), IntList(ParseJson(TextOrEmpty(reader, 5))), IntList(ParseJson(TextOrEmpty(reader, 6))),
                    skills.ValueKind == JsonValueKind.Array
                        ? skills.EnumerateArray().Select(skill => JsonInt(skill, "id")).FirstOrDefault()
                        : 0,
                    reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                    skills.ValueKind == JsonValueKind.Array
                        ? skills.EnumerateArray().Select(skill => JsonInt(skill, "exp")).FirstOrDefault()
                        : 0));
            }

            if (cats.Count > 0) return cats;
        }

        if (!TableExists(connection, "decoded_packets")) return cats;
        using var latest = connection.CreateCommand();
        latest.CommandText = "SELECT json FROM decoded_packets WHERE packet_id = 25001 AND direction = 'SC' ORDER BY rowid DESC LIMIT 1";
        if (latest.ExecuteScalar() is not string json) return cats;
        foreach (var cat in Array(ParseJson(json), "commanders"))
        {
            var uid = ValueText(Child(cat, "id"));
            if (uid.Length == 0) continue;
            cats.Add(new OwnedMeowfficer(uid, JsonInt(cat, "template_id"), JsonInt(cat, "level"), JsonInt(cat, "is_locked") != 0,
                JsonString(cat, "name"), Array(cat, "ability").Select(IntValue).ToList(),
                Array(cat, "ability_origin").Select(IntValue).ToList(),
                Array(cat, "skill").Select(skill => JsonInt(skill, "id")).FirstOrDefault(), JsonInt(cat, "exp"),
                Array(cat, "skill").Select(skill => JsonInt(skill, "exp")).FirstOrDefault()));
        }

        return cats;
    }

    /// <summary>Feedings and battle EXP since the last login list (current_commander_events), in order.</summary>
    private static IReadOnlyList<MeowfficerFeed> ReadMeowfficerFeeds(SqliteConnection connection)
    {
        var feeds = new List<MeowfficerFeed>();
        if (!TableExists(connection, "current_commander_events")) return feeds;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT kind, target_uid, exp, materials_json FROM current_commander_events ORDER BY event_id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var materials = ParseJson(TextOrEmpty(reader, 3));
            feeds.Add(new MeowfficerFeed(reader.GetString(1),
                (materials.ValueKind == JsonValueKind.Array ? materials.EnumerateArray() : [])
                .Select(material => new OwnedMeowfficer("", JsonInt(material, "template_id"), JsonInt(material, "level"), false, "",
                    IntList(ParseJson(JsonString(material, "abilities"))), [], 0, JsonInt(material, "exp")))
                .ToList(),
                reader.IsDBNull(2) ? 0 : reader.GetInt32(2)));
        }

        return feeds;
    }

    private static IReadOnlyList<MeowfficerFleetInput> ReadMeowfficerFleets(SqliteConnection connection, List<ShipState> ships)
    {
        if (!TableExists(connection, "current_fleet_ships")) return [];
        var hullByUid = ships.Where(ship => ship.Uid.Length > 0).GroupBy(ship => ship.Uid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => ShipTypeHulls.GetValueOrDefault(group.First().ShipTypeId, "?"),
                StringComparer.Ordinal);
        var hulls = SortieFleetIds.ToDictionary(id => id, _ => new List<string>());
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT fleet_id, ship_uid FROM current_fleet_ships ORDER BY fleet_id, slot_index";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (hulls.TryGetValue(reader.GetInt32(0), out var list) && hullByUid.TryGetValue(reader.GetString(1), out var hull))
                    list.Add(hull);
            }
        }

        var slots = new Dictionary<(int Fleet, int Pos), string>();
        if (TableExists(connection, "current_fleet_commanders"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT fleet_id, pos, commander_id FROM current_fleet_commanders";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                slots[(reader.GetInt32(0), reader.GetInt32(1))] = Convert.ToString(reader.GetValue(2), System.Globalization.CultureInfo.InvariantCulture) ?? "";
        }

        return SortieFleetIds
            .Select(id => new MeowfficerFleetInput(id, hulls[id], slots.GetValueOrDefault((id, 1)) ?? "",
                slots.GetValueOrDefault((id, 2)) ?? ""))
            .Where(fleet => fleet.Hulls.Count > 0 || fleet.CommandUid.Length > 0 || fleet.StaffUid.Length > 0)
            .ToList();
    }

    private static IReadOnlyList<int> IntList(JsonElement array) =>
        array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().Select(IntValue).Where(id => id > 0).ToList() : [];

    private static string TextOrEmpty(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? "" : reader.GetString(index);
}
