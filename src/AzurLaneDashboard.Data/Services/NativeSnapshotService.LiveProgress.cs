using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private const int ShipDropType = 4;

    /// <summary>
    /// The newest battle result (SC 40004) and the request just before it (CS 40003: stage and score), for
    /// automation that waits on battles. Null before any battle was captured.
    /// </summary>
    private static LatestBattleInfo? ReadLatestBattle(SqliteConnection connection, List<ShipState> ships,
        JsonElement stageMap)
    {
        if (!TableExists(connection, "decoded_packets"))
        {
            return null;
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT rowid, packet_id, json
                              FROM decoded_packets
                              WHERE packet_id IN (40003, 40004)
                              ORDER BY rowid DESC
                              LIMIT 2
                              """;
        using var reader = command.ExecuteReader();
        var rows = new List<(long RowId, int PacketId, JsonElement Json)>();
        while (reader.Read())
        {
            try
            {
                using var document = JsonDocument.Parse(reader.GetString(2));
                rows.Add((reader.GetInt64(0), reader.GetInt32(1), document.RootElement.Clone()));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        if (rows.Count == 0 || rows[0].PacketId != 40004)
        {
            return null;
        }

        var result = rows[0].Json;
        JsonElement? request = rows.Count > 1 && rows[1].PacketId == 40003 ? rows[1].Json : null;
        var stageId = request is { } sent ? JsonInt(sent, "data") : 0;
        var score = request is { } scored && scored.TryGetProperty("score", out var value) ? IntValue(value) : -1;
        var mvp = ValueText(Child(result, "mvp"));
        var byUid = ships.ToDictionary(ship => ship.Uid, StringComparer.Ordinal);
        return new LatestBattleInfo(
            rows[0].RowId,
            stageId,
            stageId > 0 ? GetStageInfo(stageId, stageMap).Label : "",
            score,
            mvp == "0" ? "" : mvp,
            mvp is "" or "0" ? "" : BattleShipName(byUid, mvp),
            Array(result, "drop_info").Count(drop => JsonInt(drop, "type") == ShipDropType));
    }

    /// <summary>The chapter last entered (SC 13102 current_chapter.id), 0 when none was captured.</summary>
    private static int ReadCurrentChapterId(SqliteConnection connection)
    {
        if (!TableExists(connection, "current_chapter_state"))
        {
            return 0;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value_json FROM current_chapter_state WHERE key = 'SC_13102'";
        if (command.ExecuteScalar() is not string json)
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonInt(Child(document.RootElement, "current_chapter"), "id");
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
