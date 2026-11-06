using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private const int SimulationShipIds = 900000;

    private static PersonalTaskDashboard BuildPersonalTasks(SqliteConnection connection, List<ShipState> ships, SnapshotMaps maps)
    {
        var tasks = ReadPlayerTasks(connection, out var basis);
        if (tasks.Count == 0) return PersonalTaskDashboard.Empty;

        var owned = ships.Where(ship => ship.GroupId > 0)
            .GroupBy(ship => ship.GroupId)
            .ToDictionary(group => group.Key, group =>
            {
                var best = group.OrderByDescending(ship => ship.Level).First();
                return new PersonalTaskShip(best.Name.Trim(), best.IconUrl, best.Rarity, best.Level);
            });
        var names = new Dictionary<int, string>();
        if (maps.Ships.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in maps.Ships.EnumerateObject().OrderBy(entry => ToInt(entry.Name)))
            {
                if (ToInt(entry.Name) >= SimulationShipIds) continue;
                var group = JsonInt(entry.Value, "group_type");
                var name = JsonString(entry.Value, "name").Trim();
                if (group > 0 && name.Length > 0) names.TryAdd(group, name);
            }
        }

        return PersonalTaskBuilder.Build(PersonalTaskBuilder.ParseChains(maps.PersonalTasks), tasks, owned, names,
            basis.Length > 0 ? $"마지막 로그인 임무 목록 · {basis}" : "마지막 로그인 임무 목록");
    }

    /// <summary>
    /// current_tasks, or, before the capture store made it, the latest login list (SC 20001) still in decoded_packets
    /// with the progress and additions after it.
    /// </summary>
    private static Dictionary<int, PlayerTask> ReadPlayerTasks(SqliteConnection connection, out string basis)
    {
        basis = "";
        var tasks = new Dictionary<int, PlayerTask>();
        if (!TableExists(connection, "decoded_packets") && !TableExists(connection, "current_tasks")) return tasks;

        long loginRow = 0;
        if (TableExists(connection, "decoded_packets"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(MAX(rowid), 0) FROM decoded_packets WHERE packet_id = 20001 AND direction = 'SC'";
            loginRow = Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }

        if (TableExists(connection, "current_tasks"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT task_id, progress, accept_time, submit_time FROM current_tasks";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                tasks[reader.GetInt32(0)] = new PlayerTask(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt64(2), reader.GetInt64(3));
        }
        else if (loginRow > 0)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT packet_id, json FROM decoded_packets
                                  WHERE rowid >= $row AND packet_id IN (20001, 20002, 20003) AND direction = 'SC'
                                  ORDER BY rowid
                                  """;
            command.Parameters.AddWithValue("$row", loginRow);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var json = ParseJson(reader.GetString(1));
                foreach (var item in Items(json, "info"))
                {
                    var id = JsonInt(item, "id");
                    if (id <= 0) continue;
                    var known = tasks.GetValueOrDefault(id);
                    tasks[id] = reader.GetInt32(0) == 20002
                        ? new PlayerTask(id, JsonInt(item, "progress"), known?.AcceptTime ?? 0, known?.SubmitTime ?? 0)
                        : new PlayerTask(id, JsonInt(item, "progress"), JsonInt(item, "accept_time"), JsonInt(item, "submit_time"));
                }
            }
        }

        if (loginRow > 0 && TableExists(connection, "current_packet_apply_log"))
        {
            using var time = connection.CreateCommand();
            time.CommandText = "SELECT MIN(applied_at) FROM current_packet_apply_log WHERE decoded_rowid = $row";
            time.Parameters.AddWithValue("$row", loginRow);
            basis = time.ExecuteScalar() as string ?? "";
        }

        return tasks;
    }
}
