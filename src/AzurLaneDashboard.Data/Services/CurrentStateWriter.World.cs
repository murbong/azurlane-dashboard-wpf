using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

/// <summary>
/// Operation Siren (대형작전). SC 33001, sent on entering it, carries the whole world state; SC 33601 then pushes
/// target progress and CS 33602 / SC 33603 claim star rewards. decoded_packets keeps only the latest 20,000
/// packets, so the entry snapshot, every target's progress and the claimed stars get tables of their own.
/// </summary>
public sealed partial class CurrentStateWriter
{
    private void EnsureWorldTables()
    {
        bool existed;
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'current_world_targets'";
            existed = exists.ExecuteScalar() is not null;
        }

        if (existed) return;
        using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                                 CREATE TABLE IF NOT EXISTS current_world_snapshot (
                                     id INTEGER PRIMARY KEY CHECK (id = 1),
                                     snapshot_rowid INTEGER,
                                     json TEXT,
                                     updated_at TEXT
                                 );
                                 CREATE TABLE current_world_targets (
                                     target_id INTEGER PRIMARY KEY,
                                     progress_json TEXT,
                                     updated_rowid INTEGER
                                 );
                                 CREATE TABLE IF NOT EXISTS current_world_target_fetch (
                                     area_id INTEGER PRIMARY KEY,
                                     stars_json TEXT,
                                     updated_rowid INTEGER
                                 );
                                 """;
            create.ExecuteNonQuery();
        }

        BackfillWorld();
    }

    /// <summary>The latest entry snapshot still in decoded_packets, and the updates and claims after it.</summary>
    private void BackfillWorld()
    {
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'decoded_packets'";
            if (exists.ExecuteScalar() is null) return;
        }

        long since;
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT COALESCE(MAX(rowid), 0) FROM decoded_packets WHERE packet_id = 33001 AND direction = 'SC'";
            since = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        if (since <= 0) return;
        var packets = new List<(long RowId, int PacketId, string Direction, string Json)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                                  SELECT rowid, packet_id, direction, json FROM decoded_packets
                                  WHERE rowid >= $since AND packet_id IN (33001, 33601, 33602, 33603)
                                  ORDER BY rowid
                                  """;
            command.Parameters.AddWithValue("$since", since);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                packets.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
        }

        using var tx = connection.BeginTransaction();
        string? claim = null;
        foreach (var (rowId, packetId, direction, text) in packets)
        {
            using var document = JsonDocument.Parse(text);
            var json = document.RootElement;
            switch (packetId, direction)
            {
                case (33001, "SC"):
                    ReplaceWorld(tx, rowId, json);
                    break;
                case (33601, "SC"):
                    UpsertWorldTargets(tx, rowId, json);
                    break;
                case (33602, "CS"):
                    claim = text;
                    break;
                case (33603, "SC") when claim is not null:
                    using (var request = JsonDocument.Parse(claim))
                        AddWorldStarClaims(tx, rowId, json, request.RootElement);
                    claim = null;
                    break;
            }
        }

        tx.Commit();
    }

    private StateApplyResult ApplyWorldSnapshot(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        if (packet.PacketId == 33001)
        {
            var count = ReplaceWorld(tx, packet.DecodedRowId, json);
            return StateApplyResult.Applied("world", $"world targets {count.ToString(CultureInfo.InvariantCulture)}",
                packet.DecodedRowId);
        }

        var updated = UpsertWorldTargets(tx, packet.DecodedRowId, json);
        return StateApplyResult.Applied("world", $"world targets updated {updated.ToString(CultureInfo.InvariantCulture)}",
            packet.DecodedRowId);
    }

    private StateApplyResult ApplyWorldStarClaim(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json,
        JsonElement request, string area)
    {
        var added = AddWorldStarClaims(tx, packet.DecodedRowId, json, request);
        return StateApplyResult.Applied(area, $"world star rewards claimed {added.ToString(CultureInfo.InvariantCulture)}",
            packet.DecodedRowId);
    }

    private int ReplaceWorld(SqliteTransaction tx, long rowId, JsonElement json)
    {
        Execute(tx,
            """
            INSERT INTO current_world_snapshot (id, snapshot_rowid, json, updated_at) VALUES (1, $rowid, $json, $now)
            ON CONFLICT(id) DO UPDATE SET snapshot_rowid = excluded.snapshot_rowid, json = excluded.json,
                updated_at = excluded.updated_at
            """,
            ("$rowid", rowId), ("$json", Json(json)), ("$now", NowText()));
        Execute(tx, "DELETE FROM current_world_targets");
        Execute(tx, "DELETE FROM current_world_target_fetch");
        var count = UpsertWorldTargets(tx, rowId, json);
        foreach (var fetch in Items(json, "target_fetch_list"))
            SetWorldStars(tx, rowId, Int(fetch, "id"), Items(fetch, "star_list").Select(Int32));
        return count;
    }

    private int UpsertWorldTargets(SqliteTransaction tx, long rowId, JsonElement json)
    {
        var count = 0;
        foreach (var target in Items(json, "target_list"))
        {
            var id = Int(target, "id");
            if (id <= 0) continue;
            var progress = Items(target, "process_list")
                .ToDictionary(item => Int(item, "trigger_id").ToString(CultureInfo.InvariantCulture), item => Int(item, "count"));
            Execute(tx,
                """
                INSERT INTO current_world_targets (target_id, progress_json, updated_rowid) VALUES ($id, $progress, $rowid)
                ON CONFLICT(target_id) DO UPDATE SET progress_json = excluded.progress_json,
                    updated_rowid = excluded.updated_rowid
                """,
                ("$id", id), ("$progress", Json(progress)), ("$rowid", rowId));
            count++;
        }

        return count;
    }

    private int AddWorldStarClaims(SqliteTransaction tx, long rowId, JsonElement response, JsonElement request)
    {
        if (Int(response, "result") != 0) return 0;
        var added = 0;
        foreach (var fetch in Items(request, "list"))
        {
            var area = Int(fetch, "id");
            if (area <= 0) continue;
            var stars = ReadWorldStars(tx, area);
            foreach (var star in Items(fetch, "star_list").Select(Int32))
                if (stars.Add(star)) added++;
            SetWorldStars(tx, rowId, area, stars);
        }

        return added;
    }

    private HashSet<int> ReadWorldStars(SqliteTransaction tx, int area)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT stars_json FROM current_world_target_fetch WHERE area_id = $area";
        command.Parameters.AddWithValue("$area", area);
        return command.ExecuteScalar() is string text
            ? JsonSerializer.Deserialize<List<int>>(text)?.ToHashSet() ?? []
            : [];
    }

    private void SetWorldStars(SqliteTransaction tx, long rowId, int area, IEnumerable<int> stars)
    {
        if (area <= 0) return;
        Execute(tx,
            """
            INSERT INTO current_world_target_fetch (area_id, stars_json, updated_rowid) VALUES ($area, $stars, $rowid)
            ON CONFLICT(area_id) DO UPDATE SET stars_json = excluded.stars_json, updated_rowid = excluded.updated_rowid
            """,
            ("$area", area), ("$stars", Json(stars.Distinct().Order().ToList())), ("$rowid", rowId));
    }
}
