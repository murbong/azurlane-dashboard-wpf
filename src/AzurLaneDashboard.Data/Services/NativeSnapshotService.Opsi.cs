using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private const int AdaptabilityAttack = 401;
    private const int AdaptabilityDurability = 402;
    private const int AdaptabilityRecovery = 403;

    private static OpsiDashboard BuildOpsi(SqliteConnection connection, SnapshotMaps maps)
    {
        var state = ReadOpsiState(connection);
        return state.IsKnown ? OpsiStarBuilder.Build(OpsiCatalog.Parse(maps.WorldTargets), state) : OpsiDashboard.Empty;
    }

    /// <summary>
    /// The capture store's world tables, or, before it made them, the latest entry snapshot (SC 33001) still in
    /// decoded_packets with the target updates (SC 33601) after it.
    /// </summary>
    private static OpsiState ReadOpsiState(SqliteConnection connection)
    {
        JsonElement world;
        var updatedAt = "";
        var progress = new Dictionary<int, IReadOnlyDictionary<int, int>>();
        var claimed = new Dictionary<int, IReadOnlySet<int>>();
        if (TableExists(connection, "current_world_snapshot") && TableExists(connection, "current_world_targets"))
        {
            using (var command = connection.CreateCommand())
            {
                // The capture time of the entry packet; the table's own time is when a backfill copied it.
                command.CommandText = TableExists(connection, "current_packet_apply_log")
                    ? """
                      SELECT s.json, COALESCE((SELECT MIN(l.applied_at) FROM current_packet_apply_log l
                                               WHERE l.decoded_rowid = s.snapshot_rowid), s.updated_at)
                      FROM current_world_snapshot s WHERE s.id = 1
                      """
                    : "SELECT json, updated_at FROM current_world_snapshot WHERE id = 1";
                using var reader = command.ExecuteReader();
                if (!reader.Read()) return new OpsiState();
                world = ParseJson(reader.GetString(0));
                updatedAt = reader.IsDBNull(1) ? "" : reader.GetString(1);
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT target_id, progress_json FROM current_world_targets";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    progress[reader.GetInt32(0)] = ParseJson(reader.GetString(1)).EnumerateObject()
                        .ToDictionary(item => ToInt(item.Name), item => IntValue(item.Value));
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT area_id, stars_json FROM current_world_target_fetch";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    claimed[reader.GetInt32(0)] = ParseJson(reader.GetString(1)).EnumerateArray().Select(IntValue).ToHashSet();
            }
        }
        else
        {
            if (!TableExists(connection, "decoded_packets")) return new OpsiState();
            long since;
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT rowid, json FROM decoded_packets WHERE packet_id = 33001 AND direction = 'SC' ORDER BY rowid DESC LIMIT 1";
                using var reader = command.ExecuteReader();
                if (!reader.Read()) return new OpsiState();
                since = reader.GetInt64(0);
                world = ParseJson(reader.GetString(1));
            }

            if (TableExists(connection, "current_packet_apply_log"))
            {
                using var time = connection.CreateCommand();
                time.CommandText = "SELECT MIN(applied_at) FROM current_packet_apply_log WHERE decoded_rowid = $row";
                time.Parameters.AddWithValue("$row", since);
                updatedAt = time.ExecuteScalar() as string ?? "";
            }

            void AddTargets(JsonElement json)
            {
                foreach (var target in Items(json, "target_list"))
                    progress[JsonInt(target, "id")] = Items(target, "process_list")
                        .ToDictionary(item => JsonInt(item, "trigger_id"), item => JsonInt(item, "count"));
            }

            AddTargets(world);
            foreach (var fetch in Items(world, "target_fetch_list"))
                claimed[JsonInt(fetch, "id")] = Array(fetch, "star_list").Select(IntValue).ToHashSet();
            using var updates = connection.CreateCommand();
            updates.CommandText = "SELECT json FROM decoded_packets WHERE rowid > $since AND packet_id = 33601 ORDER BY rowid";
            updates.Parameters.AddWithValue("$since", since);
            using var updateReader = updates.ExecuteReader();
            while (updateReader.Read()) AddTargets(ParseJson(updateReader.GetString(0)));
        }

        var inner = Child(world, "world");
        // The decoder names the world item's id "item_type" and its count "item_id".
        var items = Items(inner, "item_list")
            .GroupBy(item => JsonInt(item, "item_type"))
            .ToDictionary(group => group.Key, group => group.Sum(item => JsonInt(item, "item_id")));
        var buffs = Items(inner, "buff_list").GroupBy(buff => JsonInt(buff, "id"))
            .ToDictionary(group => group.Key, group => group.Max(buff => JsonInt(buff, "stack")));
        return new OpsiState
        {
            IsKnown = true,
            UpdatedAt = updatedAt,
            Progress = progress,
            Claimed = claimed,
            Items = items,
            Adaptability = (buffs.GetValueOrDefault(AdaptabilityAttack), buffs.GetValueOrDefault(AdaptabilityDurability),
                buffs.GetValueOrDefault(AdaptabilityRecovery))
        };
    }
}
