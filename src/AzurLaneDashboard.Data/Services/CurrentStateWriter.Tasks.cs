using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

/// <summary>
/// Tasks (임무). SC 20001 lists every task at login, submitted ones included (submit_time set); SC 20002 moves
/// progress, SC 20003 adds tasks, SC 20004 drops them, and CS 20005 / SC 20006 and SC 20012 submit them.
/// The character personal tasks read this table, so a submitted task is never dropped from it.
/// </summary>
public sealed partial class CurrentStateWriter
{
    private void EnsureTaskTable()
    {
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'current_tasks'";
            if (exists.ExecuteScalar() is not null) return;
        }

        using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                                 CREATE TABLE current_tasks (
                                     task_id INTEGER PRIMARY KEY,
                                     progress INTEGER,
                                     accept_time INTEGER,
                                     submit_time INTEGER,
                                     updated_rowid INTEGER
                                 );
                                 """;
            create.ExecuteNonQuery();
        }

        BackfillTasks();
    }

    /// <summary>The latest login list still in decoded_packets, and the task packets after it.</summary>
    private void BackfillTasks()
    {
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'decoded_packets'";
            if (exists.ExecuteScalar() is null) return;
        }

        long since;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COALESCE(MAX(rowid), 0) FROM decoded_packets WHERE packet_id = 20001 AND direction = 'SC'";
            since = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        if (since <= 0) return;
        var packets = new List<(long RowId, int PacketId, string Direction, string Json)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                                  SELECT rowid, packet_id, direction, json FROM decoded_packets
                                  WHERE rowid >= $since AND packet_id IN (20001, 20002, 20003, 20004, 20005, 20006, 20012)
                                  ORDER BY rowid
                                  """;
            command.Parameters.AddWithValue("$since", since);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                packets.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
        }

        using var tx = connection.BeginTransaction();
        var submitting = 0;
        foreach (var (rowId, packetId, direction, text) in packets)
        {
            using var document = JsonDocument.Parse(text);
            var json = document.RootElement;
            if (direction == "CS")
            {
                if (packetId == 20005) submitting = Int(json, "id");
                continue;
            }

            if (packetId == 20006)
            {
                if (Int(json, "result") == 0) SubmitTasks(tx, rowId, [submitting]);
                submitting = 0;
                continue;
            }

            ApplyTaskPacket(tx, rowId, packetId, json);
        }

        tx.Commit();
    }

    private StateApplyResult ApplyTaskSnapshot(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        var count = ApplyTaskPacket(tx, packet.DecodedRowId, packet.PacketId, json);
        return StateApplyResult.Applied("tasks",
            $"tasks {packet.PacketId.ToString(CultureInfo.InvariantCulture)} {count.ToString(CultureInfo.InvariantCulture)}",
            packet.DecodedRowId);
    }

    private StateApplyResult ApplyTaskSubmit(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json,
        JsonElement request, string area)
    {
        if (Int(json, "result") == 0) SubmitTasks(tx, packet.DecodedRowId, [Int(request, "id")]);
        return StateApplyResult.Applied(area, "task submitted", packet.DecodedRowId);
    }

    private int ApplyTaskPacket(SqliteTransaction tx, long rowId, int packetId, JsonElement json)
    {
        switch (packetId)
        {
            case 20001:
                Execute(tx, "DELETE FROM current_tasks");
                return UpsertTasks(tx, rowId, json, withTimes: true);
            case 20002:
                return UpsertTasks(tx, rowId, json, withTimes: false);
            case 20003:
                return UpsertTasks(tx, rowId, json, withTimes: true);
            case 20004:
                var removed = 0;
                foreach (var id in Items(json, "id_list").Select(Int32))
                {
                    // Submitted tasks stay: they are the record that a character chain step is done.
                    Execute(tx, "DELETE FROM current_tasks WHERE task_id = $id AND COALESCE(submit_time, 0) = 0", ("$id", id));
                    removed++;
                }

                return removed;
            case 20012:
                var ids = Items(json, "id_list").Select(Int32).ToList();
                SubmitTasks(tx, rowId, ids);
                return ids.Count;
            default:
                return 0;
        }
    }

    private int UpsertTasks(SqliteTransaction tx, long rowId, JsonElement json, bool withTimes)
    {
        var count = 0;
        foreach (var task in Items(json, "info"))
        {
            var id = Int(task, "id");
            if (id <= 0) continue;
            if (withTimes)
            {
                Execute(tx,
                    """
                    INSERT INTO current_tasks (task_id, progress, accept_time, submit_time, updated_rowid)
                    VALUES ($id, $progress, $accept, $submit, $rowid)
                    ON CONFLICT(task_id) DO UPDATE SET progress = excluded.progress, accept_time = excluded.accept_time,
                        submit_time = excluded.submit_time, updated_rowid = excluded.updated_rowid
                    """,
                    ("$id", id), ("$progress", Int(task, "progress")), ("$accept", Int(task, "accept_time")),
                    ("$submit", Int(task, "submit_time")), ("$rowid", rowId));
            }
            else
            {
                Execute(tx,
                    """
                    INSERT INTO current_tasks (task_id, progress, accept_time, submit_time, updated_rowid)
                    VALUES ($id, $progress, 0, 0, $rowid)
                    ON CONFLICT(task_id) DO UPDATE SET progress = excluded.progress, updated_rowid = excluded.updated_rowid
                    """,
                    ("$id", id), ("$progress", Int(task, "progress")), ("$rowid", rowId));
            }

            count++;
        }

        return count;
    }

    private void SubmitTasks(SqliteTransaction tx, long rowId, IEnumerable<int> ids)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var id in ids.Where(id => id > 0))
        {
            Execute(tx,
                """
                INSERT INTO current_tasks (task_id, progress, accept_time, submit_time, updated_rowid)
                VALUES ($id, 0, 0, $now, $rowid)
                ON CONFLICT(task_id) DO UPDATE SET submit_time = $now, updated_rowid = excluded.updated_rowid
                """,
                ("$id", id), ("$now", now), ("$rowid", rowId));
        }
    }
}
