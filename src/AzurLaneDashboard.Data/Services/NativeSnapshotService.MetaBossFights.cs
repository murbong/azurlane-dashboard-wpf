using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private sealed record MetaFightSource(long Id, string Owner, string ShipsJson, string? GearJson, string? FinishJson,
        string StartedAt, string FinishedAt);

    /// <summary>
    /// Finished META fights from current_meta_boss_fights, or straight from decoded_packets when the capture store
    /// has not created that table yet. A sortie still open names no boss (only the finish carries the boss's enemy
    /// id), so it is left out.
    /// </summary>
    private static IReadOnlyList<MetaBossFightRecord> ReadMetaBossFights(SqliteConnection connection, SnapshotMaps maps)
    {
        var sources = TableExists(connection, "current_meta_boss_fights")
            ? ReadStoredMetaFights(connection)
            : ReadPacketMetaFights(connection);
        var account = AccountUid(connection);
        return sources.Where(source => source.FinishJson is not null)
            .Select(source => ToMetaFight(source, account, maps))
            .Where(fight => fight.IsFinished)
            .ToList();
    }

    private static List<MetaFightSource> ReadStoredMetaFights(SqliteConnection connection)
    {
        var sources = new List<MetaFightSource>();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT fight_rowid, boss_owner, ship_uids_json, gear_json, finish_json, started_at, finished_at
                              FROM current_meta_boss_fights ORDER BY fight_rowid
                              """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sources.Add(new MetaFightSource(reader.GetInt64(0), NullableText(reader, 1) ?? "", NullableText(reader, 2) ?? "[]",
                NullableText(reader, 3), NullableText(reader, 4), NullableText(reader, 5) ?? "", NullableText(reader, 6) ?? ""));
        }

        return sources;
    }

    // Same pairing as the capture store's: each finish closes the latest open sortie on the same boss.
    private static List<MetaFightSource> ReadPacketMetaFights(SqliteConnection connection)
    {
        var sources = new List<MetaFightSource>();
        if (!TableExists(connection, "decoded_packets")) return sources;
        using var command = connection.CreateCommand();
        // The apply log holds when each packet arrived; decoded_packets itself has no time.
        command.CommandText = TableExists(connection, "current_packet_apply_log")
            ? """
              SELECT p.rowid, p.packet_id, p.json, COALESCE(l.applied_at, '') FROM decoded_packets p
              LEFT JOIN current_packet_apply_log l ON l.decoded_rowid = p.rowid
              WHERE p.direction = 'CS' AND p.packet_id IN (40001, 40003) AND p.json LIKE '%"system":50%'
              ORDER BY p.rowid
              """
            : """
              SELECT rowid, packet_id, json, '' FROM decoded_packets
              WHERE direction = 'CS' AND packet_id IN (40001, 40003) AND json LIKE '%"system":50%'
              ORDER BY rowid
              """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var json = ParseJson(reader.GetString(2));
            if (JsonInt(json, "system") != CurrentStateWriter.MetaBossSystem) continue;
            var owner = ValueText(Child(json, "data"));
            var time = reader.GetString(3);
            if (reader.GetInt32(1) == 40001)
            {
                var uids = Array(json, "ship_id_list").Select(ValueText).Where(uid => uid.Length > 0).ToList();
                sources.Add(new MetaFightSource(reader.GetInt64(0), owner, JsonSerializer.Serialize(uids), null, null, time, ""));
                continue;
            }

            var open = sources.FindLastIndex(source => source.FinishJson is null && source.Owner == owner);
            var finish = reader.GetString(2);
            if (open >= 0) sources[open] = sources[open] with { FinishJson = finish, FinishedAt = time };
            else sources.Add(new MetaFightSource(reader.GetInt64(0), owner, "[]", null, finish, time, time));
        }

        return sources;
    }

    private static MetaBossFightRecord ToMetaFight(MetaFightSource source, string account, SnapshotMaps maps)
    {
        var finish = ParseJson(source.FinishJson ?? "{}");
        var enemy = Array(finish, "enemy_info").Select(item => Fields(item, EnemyNames)).FirstOrDefault()
                    ?? new Dictionary<string, long>();
        var stats = Array(finish, "statistics")
            .Select(item => Fields(item, StatisticNames))
            .Where(item => item.GetValueOrDefault("ship_id") > 0)
            .Select(item => new MetaFightShipStat(
                item["ship_id"].ToString(CultureInfo.InvariantCulture),
                item.GetValueOrDefault("damage_cause"),
                item.GetValueOrDefault("damage_caused"),
                (int)item.GetValueOrDefault("hp_rest"),
                item.GetValueOrDefault("max_damage_once"),
                (int)item.GetValueOrDefault("ship_gear_score")))
            .ToList();
        return new MetaBossFightRecord
        {
            Id = source.Id,
            OwnerUid = source.Owner,
            IsOwnBoss = account.Length > 0 && source.Owner == account,
            FinishedAt = DateTime.TryParseExact(FirstNonEmpty(source.FinishedAt, source.StartedAt), "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null,
            Score = JsonInt(finish, "score"),
            TotalSeconds = JsonInt(finish, "total_time"),
            BossHpLeft = JsonInt(finish, "boss_hp"),
            BossEnemyId = (int)enemy.GetValueOrDefault("enemy_id"),
            BossDamage = enemy.GetValueOrDefault("damage_taken"),
            BossTotalHp = enemy.GetValueOrDefault("total_hp"),
            AutoAtStart = JsonInt(finish, "auto_before") == 1,
            AutoSwitches = JsonInt(finish, "auto_switch_time"),
            SortieUids = StringList(source.ShipsJson),
            Stats = stats,
            Gear = source.GearJson is null ? null : ReadFightGear(source.GearJson, maps)
        };
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<MetaFightGear>> ReadFightGear(string json, SnapshotMaps maps)
    {
        var result = new Dictionary<string, IReadOnlyList<MetaFightGear>>(StringComparer.Ordinal);
        var root = ParseJson(json);
        if (root.ValueKind != JsonValueKind.Object) return result;
        foreach (var ship in root.EnumerateObject().Where(ship => ship.Value.ValueKind == JsonValueKind.Object))
        {
            result[ship.Name] = ship.Value.EnumerateObject()
                .Select(slot => (Slot: int.TryParse(slot.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0,
                    TemplateId: IntValue(slot.Value)))
                .Where(slot => slot.Slot > 0)
                .OrderBy(slot => slot.Slot)
                .Select(slot =>
                {
                    var info = MapObject(maps.Equipment, slot.TemplateId);
                    return new MetaFightGear(slot.Slot, slot.TemplateId,
                        slot.TemplateId > 0 ? FirstNonEmpty(JsonString(info, "name"), $"equip:{slot.TemplateId}") : "",
                        EquipmentEnhanceLevel(JsonInt(info, "enhance_level")));
                })
                .ToList();
        }

        return result;
    }

    /// <summary>Display facts for every ship a fight names: Regal hull and position when matched, else the dock's.</summary>
    private static IReadOnlyDictionary<string, MetaFightShipInfo> MetaFightShips(
        IReadOnlyList<MetaBossFightRecord> fights, List<ShipState> ships, IReadOnlyList<FleetPlannerShip> plannerShips)
    {
        var planner = plannerShips.ToDictionary(ship => ship.Uid, StringComparer.Ordinal);
        var dock = ships.Where(ship => ship.Uid.Length > 0).GroupBy(ship => ship.Uid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var result = new Dictionary<string, MetaFightShipInfo>(StringComparer.Ordinal);
        foreach (var uid in fights.SelectMany(fight => fight.Stats.Select(stat => stat.Uid)).Distinct())
        {
            if (planner.TryGetValue(uid, out var ship))
                result[uid] = new MetaFightShipInfo(uid, ship.Name, ship.IconUrl, ship.Rarity, ship.Hull, ship.Position,
                    ship.Level, ship.RegalUid);
            else if (dock.TryGetValue(uid, out var state))
                result[uid] = new MetaFightShipInfo(uid, state.Name.Trim(), state.IconUrl, state.Rarity, "", "", state.Level, "");
        }

        return result;
    }

    private static string AccountUid(SqliteConnection connection) =>
        BuildCurrentCommanderProfile(connection).GetValueOrDefault("id") ?? "";

    private static readonly Dictionary<int, string> StatisticNames = new()
    {
        [1] = "ship_id", [2] = "damage_cause", [3] = "damage_caused", [4] = "hp_rest", [5] = "max_damage_once",
        [6] = "ship_gear_score"
    };

    private static readonly Dictionary<int, string> EnemyNames = new()
    {
        [1] = "enemy_id", [2] = "damage_taken", [3] = "total_hp"
    };

    // Named fields, or the protobuf hex older captures stored before CS 40003 statistics were decoded.
    private static Dictionary<string, long> Fields(JsonElement item, IReadOnlyDictionary<int, string> names)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        if (item.ValueKind != JsonValueKind.Object) return result;
        if (item.TryGetProperty("raw", out var raw) && raw.ValueKind == JsonValueKind.String)
        {
            foreach (var (number, value) in VarintFields(raw.GetString() ?? ""))
                if (names.TryGetValue(number, out var name)) result[name] = value;
            return result;
        }

        foreach (var name in names.Values) result[name] = JsonLong(item, name);
        return result;
    }

    private static IEnumerable<(int Number, long Value)> VarintFields(string hex)
    {
        byte[] bytes;
        try { bytes = Convert.FromHexString(hex); }
        catch (FormatException) { yield break; }

        var index = 0;
        while (index < bytes.Length)
        {
            if (!TryVarint(bytes, ref index, out var key)) yield break;
            var wire = (int)(key & 7);
            var number = (int)(key >> 3);
            if (wire == 0)
            {
                if (!TryVarint(bytes, ref index, out var value)) yield break;
                yield return (number, (long)value);
            }
            else if (wire == 2)
            {
                if (!TryVarint(bytes, ref index, out var length)) yield break;
                index += (int)length;
            }
            else
            {
                yield break;
            }
        }
    }

    private static bool TryVarint(byte[] bytes, ref int index, out ulong value)
    {
        value = 0;
        for (var shift = 0; shift < 64 && index < bytes.Length; shift += 7)
        {
            var next = bytes[index++];
            value |= (ulong)(next & 0x7f) << shift;
            if (next < 0x80) return true;
        }

        return false;
    }

    private static IReadOnlyList<string> StringList(string json)
    {
        var root = ParseJson(json);
        return root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().Select(ValueText).Where(uid => uid.Length > 0 && uid != "0").ToList()
            : [];
    }

    private static string? NullableText(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
}
