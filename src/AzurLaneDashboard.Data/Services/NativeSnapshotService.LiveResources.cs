using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

/// <summary>
/// Oil and courtyard food followed from packets for automation, with the client's rules (see
/// <see cref="OilEstimate"/> and <see cref="DormFoodState"/>) instead of reading the numbers off the screen.
/// </summary>
public sealed partial class NativeSnapshotService
{
    private const int OilResourceType = 2;
    private const int OilFieldResourceType = 5;
    // Responses whose rewards are added on the client (drop lists, award lists, research and mission rewards).
    private static readonly int[] OilRewardPackets = [11203, 13006, 15003, 20006, 20012, 40008, 63004, 63016, 64006, 64008];
    // Expedition types of boss stages: loop chapters cap their battles at use_oil_limit[2], others at [1].
    private static readonly HashSet<int> BossStageTypes = [94, 99];
    // dorm_data_template: every level holds 40000 and eats 5 every 15 s per the training-ship ratio.
    private const int DormBaseCapacity = 40000;
    private const int DormConsume = 5;
    private const int DormTickSeconds = 15;
    // gameset dorm_food_ratio_by_N in percent, for N ships training on floor 1.
    private static readonly int[] DormFoodRatio = [0, 100, 180, 240, 280, 320, 360];
    private const int DormTrainingFloor = 1;

    private static OilEstimate? ReadOilEstimate(SqliteConnection connection, List<ShipState> ships, SnapshotMaps maps)
    {
        if (!TableExists(connection, "decoded_packets") || ReadOilSync(connection) is not { } sync)
        {
            return null;
        }

        var byUid = new Dictionary<string, ShipState>(StringComparer.Ordinal);
        foreach (var ship in ships) byUid.TryAdd(ship.Uid, ship);
        var costs = maps.OilCosts;

        // The chapter and stage in play at the sync carry over to the battles after it.
        var chapter = 0;
        var loop = false;
        foreach (var entered in ReadPacketsBefore(connection, 13102, sync.RowId, 5))
        {
            if (EnteredChapter(entered) is { } found)
            {
                (chapter, loop) = found;
                break;
            }
        }

        var stageType = ReadPacketsBefore(connection, 40003, sync.RowId, 1)
            .Select(request => GetStageInfo(JsonInt(request, "data"), maps.Stages).Type)
            .FirstOrDefault();
        var oil = sync.Oil;
        var field = sync.Field;
        int spent = 0, gained = 0, battles = 0;
        var harvesting = false;
        var starting = 0;

        using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT packet_id, json
                               FROM decoded_packets
                               WHERE rowid > $row AND packet_id IN
                                   (13102, 40003, 40004, 11013, 11014, 13003, 13004, {string.Join(", ", OilRewardPackets)})
                               ORDER BY rowid
                               """;
        command.Parameters.AddWithValue("$row", sync.RowId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var json = ParseJson(reader.GetString(1));
            switch (reader.GetInt32(0))
            {
                case 13102 when EnteredChapter(json) is { } entered:
                {
                    (chapter, loop) = entered;
                    var config = MapObject(Child(costs, "chapters"), chapter);
                    // Chapter:getConfig reads the loop table first and falls back to the base table.
                    var cost = loop && JsonInt(config, "loop_oil") > 0 ? JsonInt(config, "loop_oil") : JsonInt(config, "oil");
                    spent += cost;
                    break;
                }
                case 40003:
                    stageType = GetStageInfo(JsonInt(json, "data"), maps.Stages).Type;
                    break;
                case 40004:
                    battles++;
                    spent += BattleOilCost(json, byUid, costs, chapter, loop, stageType);
                    gained += SumOilRewards(json);
                    break;
                case 11013:
                    harvesting = JsonInt(json, "type") == OilResourceType;
                    break;
                case 11014:
                    // HarvestResourceCommand: the oil field's stock moves into the oil (as much as storage takes).
                    if (harvesting && JsonInt(json, "result") == 0)
                    {
                        gained += field;
                        field = 0;
                    }

                    harvesting = false;
                    break;
                case 13003:
                    starting = JsonInt(json, "id");
                    break;
                case 13004:
                    // EventStartCommand.OnStart: the commission's oil (collection_template.oil, mostly 0).
                    if (JsonInt(json, "result") == 0)
                    {
                        spent += JsonInt(MapObject(maps.Commissions, starting), "oil");
                    }

                    break;
                case var rewards when OilRewardPackets.Contains(rewards) && JsonInt(json, "result") == 0:
                    gained += SumOilRewards(json);
                    break;
            }
        }

        oil += gained - spent;
        return new OilEstimate(oil, sync.Oil, sync.RowId, battles, spent, gained);
    }

    private static (long RowId, int Oil, int Field)? ReadOilSync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT rowid, json
                              FROM decoded_packets
                              WHERE packet_id IN (11003, 11004)
                              ORDER BY rowid DESC
                              LIMIT 50
                              """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var resources = Items(ParseJson(reader.GetString(1)), "resource_list").ToList();
            var resource = resources.FirstOrDefault(item => JsonInt(item, "type") == OilResourceType);
            if (resource.ValueKind == JsonValueKind.Object)
            {
                var field = resources.FirstOrDefault(item => JsonInt(item, "type") == OilFieldResourceType);
                return (reader.GetInt64(0), JsonInt(resource, "num"), JsonInt(field, "num"));
            }
        }

        return null;
    }

    private static List<JsonElement> ReadPacketsBefore(SqliteConnection connection, int packetId, long rowId, int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT json
                              FROM decoded_packets
                              WHERE packet_id = $id AND rowid < $row
                              ORDER BY rowid DESC
                              LIMIT $limit
                              """;
        command.Parameters.AddWithValue("$id", packetId);
        command.Parameters.AddWithValue("$row", rowId);
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var rows = new List<JsonElement>();
        while (reader.Read()) rows.Add(ParseJson(reader.GetString(0)));
        return rows;
    }

    // SC 13102 with a chapter: the entry that charges the chapter's oil; loop_flag 1 is a re-run.
    private static (int Chapter, bool Loop)? EnteredChapter(JsonElement json)
    {
        var current = Child(json, "current_chapter");
        return JsonInt(json, "result") == 0 && JsonInt(current, "id") is > 0 and var id
            ? (id, JsonInt(current, "loop_flag") == 1)
            : null;
    }

    // Chapter:getFleetCost: each ship's floor(oil_at_end * fight_oil_ratio / 10000); in a loop chapter the fleet
    // pays at most use_oil_limit (boss or normal) less its oil_at_start.
    private static int BattleOilCost(JsonElement result, Dictionary<string, ShipState> byUid, JsonElement costs,
        int chapter, bool loop, int stageType)
    {
        int start = 0, end = 0;
        foreach (var item in Items(result, "ship_exp_list"))
        {
            if (!byUid.TryGetValue(ValueText(Child(item, "ship_id")), out var ship)) continue;
            var oil = MapObject(Child(costs, "ships"), ship.TemplateId);
            if (oil.ValueKind != JsonValueKind.Array || oil.GetArrayLength() < 2) continue;
            var ratio = IntValue(MapObject(Child(costs, "levels"), ship.Level));
            start += IntValue(oil[0]);
            end += IntValue(oil[1]) * (ratio > 0 ? ratio : 10000) / 10000;
        }

        if (!loop) return end;
        var config = MapObject(Child(costs, "chapters"), chapter);
        var limits = (Array(config, "loop_limit").Any() ? Array(config, "loop_limit") : Array(config, "limit"))
            .Select(IntValue)
            .ToArray();
        if (limits.Length == 0) return end;
        var limit = BossStageTypes.Contains(stageType) && limits.Length > 1 ? limits[1] : limits[0];
        return Math.Max(0, Math.Min(limit - start, end));
    }

    private static bool IsOilDrop(JsonElement drop) =>
        JsonInt(drop, "type") == 1 && JsonInt(drop, "id") == OilResourceType;

    // Commission, mission and daily rewards: every {type 1, id 2} entry anywhere in the packet is oil.
    private static int SumOilRewards(JsonElement node) => node.ValueKind switch
    {
        JsonValueKind.Object => (IsOilDrop(node) ? JsonInt(node, "number") : 0) +
                                node.EnumerateObject().Sum(property => SumOilRewards(property.Value)),
        JsonValueKind.Array => node.EnumerateArray().Sum(SumOilRewards),
        _ => 0
    };

    private static DormFoodState? ReadDormFood(SqliteConnection connection)
    {
        if (!TableExists(connection, "decoded_packets"))
        {
            return null;
        }

        long loginRow;
        JsonElement login;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                                  SELECT rowid, json FROM decoded_packets
                                  WHERE packet_id = 19001 ORDER BY rowid DESC LIMIT 1
                                  """;
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            loginRow = reader.GetInt64(0);
            login = ParseJson(reader.GetString(1));
        }

        var food = JsonInt(login, "food");
        var capacity = DormBaseCapacity + JsonInt(login, "food_max_increase");
        long next = JsonInt(login, "next_timestamp");
        var training = ReadDormFloors(connection).Values.Count(floor => floor == DormTrainingFloor);
        var baseRow = loginRow;
        (int Item, int Count)? snackUse = null;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                                  SELECT rowid, packet_id, json
                                  FROM decoded_packets
                                  WHERE rowid > $row AND packet_id IN (19027, 15002, 15003)
                                  ORDER BY rowid
                                  """;
            command.Parameters.AddWithValue("$row", loginRow);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var json = ParseJson(reader.GetString(2));
                var ok = JsonInt(json, "result") == 0;
                switch (reader.GetInt32(1))
                {
                    case 19027:
                        // The settlement: food eaten since the last one and when the next tick falls.
                        food = Math.Max(0, food - JsonInt(json, "food"));
                        if (JsonInt(json, "next_timestamp") is > 0 and var tick) next = tick;
                        baseRow = reader.GetInt64(0);
                        break;
                    case 15002:
                        snackUse = (JsonInt(json, "id"), Math.Max(1, JsonInt(json, "count")));
                        break;
                    case 15003 when ok && snackUse is { } used:
                        // AddFoodCommand: the snack's food, up to the capacity.
                        if (DormFoodState.SnackFood.TryGetValue(used.Item, out var value))
                        {
                            food = Math.Min(capacity, food + value * used.Count);
                            baseRow = reader.GetInt64(0);
                        }

                        snackUse = null;
                        break;
                }
            }
        }

        var ratio = DormFoodRatio[Math.Min(training, DormFoodRatio.Length - 1)];
        return new DormFoodState(food, capacity, training, ratio * DormConsume / 100, DormTickSeconds, next,
            baseRow, ReadSnackCounts(connection));
    }

    private static HashSet<int> TrainingShips(JsonElement login) =>
        DormFloors(login).Where(pair => pair.Value == DormTrainingFloor).Select(pair => pair.Key).ToHashSet();

    // SC 19001 ship_list (id, floor); captures decoded before its schema read it as varints in tag/value pairs.
    private static Dictionary<int, int> DormFloors(JsonElement login)
    {
        var floors = new Dictionary<int, int>();
        foreach (var ship in Items(login, "ship_list"))
        {
            floors[JsonInt(ship, "id")] = JsonInt(ship, "floor");
        }

        var flat = Array(login, "ship_id_list").Select(IntValue).ToArray();
        var id = 0;
        for (var index = 0; index + 1 < flat.Length; index += 2)
        {
            switch (flat[index])
            {
                case 8:
                    id = flat[index + 1];
                    break;
                case 16:
                    floors[id] = flat[index + 1];
                    break;
            }
        }

        return floors;
    }

    /// <summary>Which ships are in the courtyard and on which floor: SC 19001, then placements and exits.</summary>
    private static Dictionary<string, int> ReadDormFloors(SqliteConnection connection)
    {
        var floors = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!TableExists(connection, "decoded_packets")) return floors;
        long loginRow;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT rowid, json FROM decoded_packets WHERE packet_id = 19001 ORDER BY rowid DESC LIMIT 1";
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return floors;
            loginRow = reader.GetInt64(0);
            foreach (var (id, floor) in DormFloors(ParseJson(reader.GetString(1))))
                floors[id.ToString(System.Globalization.CultureInfo.InvariantCulture)] = floor;
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                                  SELECT packet_id, json FROM decoded_packets
                                  WHERE rowid > $row AND packet_id IN (19002, 19003, 19004, 19005)
                                  ORDER BY rowid
                                  """;
            command.Parameters.AddWithValue("$row", loginRow);
            using var reader = command.ExecuteReader();
            (string Ship, int Floor)? placing = null;
            var leaving = "";
            while (reader.Read())
            {
                var json = ParseJson(reader.GetString(1));
                var ok = JsonInt(json, "result") == 0;
                switch (reader.GetInt32(0))
                {
                    case 19002:
                        placing = (ValueText(Child(json, "ship_id")), JsonInt(json, "type"));
                        break;
                    case 19003 when ok && placing is { } placed:
                        floors[placed.Ship] = placed.Floor;
                        placing = null;
                        break;
                    case 19004:
                        leaving = ValueText(Child(json, "ship_id"));
                        break;
                    case 19005 when ok:
                        floors.Remove(leaving);
                        break;
                }
            }
        }

        return floors;
    }

    // Ship.ENERGY_RECOVER_TIME and the morale rules of BayProxy.recoverAllShipEnergy.
    private const int MoraleTickSeconds = 360;
    private const int MoraleCap = 119;
    private const int CourtyardMoraleCap = 150;

    /// <summary>
    /// Morale the client has added since the last recovery the server reported (SC 12031): every 6 minutes 2 (3
    /// married) up to 119, and in the courtyard up to 150 plus 2 on floor 1 or 3 on floor 2. Battles' costs are
    /// already taken off; packets carry no time, so recovery comes after them.
    /// </summary>
    private sealed record MoraleRecovery(int Ticks, IReadOnlyDictionary<string, int> Floors)
    {
        public int Apply(int energy, string shipUid, bool married)
        {
            var floor = Floors.GetValueOrDefault(shipUid);
            var cap = floor > 0 ? CourtyardMoraleCap : MoraleCap;
            var point = married ? 3 : 2;
            var addition = floor switch { 1 => 2, 2 => 3, _ => 0 };
            for (var tick = 0; tick < Ticks; tick++)
            {
                var next = Math.Min(energy + Math.Max(Math.Min(point, cap - energy), 0) + addition, CourtyardMoraleCap);
                if (next == energy) break;
                energy = next;
            }

            return energy;
        }
    }

    private static MoraleRecovery ReadMoraleRecovery(SqliteConnection connection)
    {
        var floors = ReadDormFloors(connection);
        if (!TableExists(connection, "decoded_packets")) return new MoraleRecovery(0, floors);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM decoded_packets WHERE packet_id = 12031 ORDER BY rowid DESC LIMIT 1";
        if (command.ExecuteScalar() is not string json) return new MoraleRecovery(0, floors);
        var first = JsonInt(ParseJson(json), "energy_auto_increase_time") + (long)MoraleTickSeconds;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ticks = now < first ? 0 : (int)Math.Min(1000, (now - first) / MoraleTickSeconds + 1);
        return new MoraleRecovery(ticks, floors);
    }

    // SC 15001's snack counts less the snacks used since; other ways of getting snacks are not followed.
    private static Dictionary<int, int> ReadSnackCounts(SqliteConnection connection)
    {
        var counts = DormFoodState.SnackFood.Keys.ToDictionary(id => id, _ => 0);
        long itemsRow;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                                  SELECT rowid, json FROM decoded_packets
                                  WHERE packet_id = 15001 ORDER BY rowid DESC LIMIT 1
                                  """;
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return counts;
            itemsRow = reader.GetInt64(0);
            foreach (var item in Items(ParseJson(reader.GetString(1)), "item_list"))
            {
                if (counts.ContainsKey(JsonInt(item, "id"))) counts[JsonInt(item, "id")] = JsonInt(item, "count");
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                                  SELECT packet_id, json FROM decoded_packets
                                  WHERE rowid > $row AND packet_id IN (15002, 15003)
                                  ORDER BY rowid
                                  """;
            command.Parameters.AddWithValue("$row", itemsRow);
            using var reader = command.ExecuteReader();
            (int Item, int Count)? pending = null;
            while (reader.Read())
            {
                var json = ParseJson(reader.GetString(1));
                if (reader.GetInt32(0) == 15002)
                {
                    pending = (JsonInt(json, "id"), Math.Max(1, JsonInt(json, "count")));
                }
                else if (pending is { } used && JsonInt(json, "result") == 0 && counts.ContainsKey(used.Item))
                {
                    counts[used.Item] = Math.Max(0, counts[used.Item] - used.Count);
                    pending = null;
                }
            }
        }

        return counts;
    }
}
