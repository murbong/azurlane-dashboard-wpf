using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static List<PacketRecord> ReadPackets(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var packets = new List<PacketRecord>();
        if (!TableExists(connection, "decoded_packets"))
        {
            return packets;
        }

        var rowIds = ReadRelevantPacketRowIds(connection);
        if (rowIds.Count == 0)
        {
            return packets;
        }

        using var command = connection.CreateCommand();
        var rowIdList = string.Join(",", rowIds);
        command.CommandText = """
                              SELECT rowid, source, direction, packet_id, packet_name, "index" AS packet_index, json
                              FROM decoded_packets
                              WHERE rowid IN ($rowIds)
                              ORDER BY rowid
                              """.Replace("$rowIds", rowIdList, StringComparison.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = Text(reader, "json");
            JsonElement root = default;
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    using var document = JsonDocument.Parse(json);
                    root = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    root = default;
                }
            }

            packets.Add(new PacketRecord(
                Int(reader, "rowid"),
                Text(reader, "source"),
                Text(reader, "direction").ToUpperInvariant(),
                Int(reader, "packet_id"),
                Text(reader, "packet_name"),
                Int(reader, "packet_index"),
                root));
        }

        return packets;
    }

    private static List<int> ReadRelevantPacketRowIds(SqliteConnection connection)
    {
        var rows = new HashSet<int>();
        var latestShipListRowId = ScalarInt(
            connection,
            "SELECT COALESCE(MAX(rowid), 0) FROM decoded_packets WHERE packet_id = 12001 AND direction = 'SC'");

        if (latestShipListRowId > 0)
        {
            var shipStateIds = ShipListPacketIds
                .Concat(SpWeaponStatePacketIds)
                .Concat(ShipChangePacketIds)
                .ToArray();
            AddRows(
                rows,
                connection,
                $"""
                 SELECT rowid
                 FROM decoded_packets
                 WHERE rowid >= $start
                   AND packet_id IN ({SqlIntList(shipStateIds)})
                 """,
                ("$start", latestShipListRowId));
        }

        var latestFleetSnapshotRowId = LatestPacketRowId(connection, 12101, "SC");
        AddRows(
            rows,
            connection,
            $"""
             SELECT rowid
             FROM decoded_packets
             WHERE rowid >= $start
               AND packet_id IN ({SqlIntList(FleetStatePacketIds)})
             """,
            ("$start", latestFleetSnapshotRowId > 0 ? latestFleetSnapshotRowId : 0));

        foreach (var packetId in LatestStatePacketIds.Concat(new[] { 11003, 11004, 14001, 14101 }))
        {
            AddLatestPacketRow(rows, connection, packetId, "SC");
        }

        var latestBaseRows = new List<int>();
        foreach (var packetId in SystemBasePacketIds)
        {
            var rowId = LatestPacketRowId(connection, packetId, "SC");
            if (rowId > 0)
            {
                latestBaseRows.Add(rowId);
            }
        }

        if (latestBaseRows.Count > 0)
        {
            AddRows(
                rows,
                connection,
                $"""
                 SELECT rowid
                 FROM decoded_packets
                 WHERE rowid >= $start
                   AND packet_id IN ({SqlIntList(SystemReconstructionPacketIds)})
                 """,
                ("$start", latestBaseRows.Min()));
        }

        var recentBattles = ReadRecentBattleResultRows(connection);
        foreach (var (rowId, source) in recentBattles)
        {
            rows.Add(rowId);
            AddRows(
                rows,
                connection,
                """
                SELECT rowid
                FROM decoded_packets
                WHERE rowid <= $rowId
                  AND source = $source
                  AND packet_id = 40003
                  AND direction = 'CS'
                ORDER BY rowid DESC
                LIMIT 1
                """,
                ("$rowId", rowId),
                ("$source", source));
        }

        return rows.OrderBy(row => row).ToList();
    }

    private static List<(int RowId, string Source)> ReadRecentBattleResultRows(SqliteConnection connection)
    {
        var rows = new List<(int RowId, string Source)>();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT rowid, source
                              FROM decoded_packets
                              WHERE packet_id = 40004
                                AND direction = 'SC'
                              ORDER BY rowid DESC
                              LIMIT $limit
                              """;
        command.Parameters.AddWithValue("$limit", RecentBattleResultLimit);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((Int(reader, "rowid"), Text(reader, "source")));
        }

        return rows;
    }

    private static void AddLatestPacketRow(HashSet<int> rows, SqliteConnection connection, int packetId,
        string direction)
    {
        var rowId = LatestPacketRowId(connection, packetId, direction);
        if (rowId > 0)
        {
            rows.Add(rowId);
        }
    }

    private static int LatestPacketRowId(SqliteConnection connection, int packetId, string direction)
    {
        return ScalarInt(
            connection,
            """
            SELECT COALESCE(MAX(rowid), 0)
            FROM decoded_packets
            WHERE packet_id = $packetId
              AND direction = $direction
            """,
            ("$packetId", packetId),
            ("$direction", direction.ToUpperInvariant()));
    }

    private static void AddRows(HashSet<int> rows, SqliteConnection connection, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture));
        }
    }

    private static int ScalarInt(SqliteConnection connection, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string SqlIntList(IEnumerable<int> values)
    {
        return string.Join(",",
            values.Distinct().OrderBy(value => value).Select(value => value.ToString(CultureInfo.InvariantCulture)));
    }

    private static (List<PacketStatRow> Stats, List<PacketStatRow> Gaps) ReadPacketStats(
        SqliteConnection connection)
    {
        var stats = new List<PacketStatRow>();
        var gaps = new List<PacketStatRow>();
        if (!TableExists(connection, "decoded_packets"))
        {
            return (stats, gaps);
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT packet_id, packet_name, COUNT(*) AS count
                              FROM decoded_packets
                              GROUP BY packet_id, packet_name
                              ORDER BY count DESC
                              """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var packetId = Int(reader, "packet_id");
            var row = new PacketStatRow
            {
                PacketId = packetId.ToString(CultureInfo.InvariantCulture),
                PacketName = Text(reader, "packet_name"),
                DecodeStatus = PacketMetadata.DecodeStatus(packetId),
                Description = PacketMetadata.Description(packetId),
                Count = Int(reader, "count")
            };
            if (stats.Count < 30)
            {
                stats.Add(row);
            }

            if (gaps.Count < 30 && PacketMetadata.IsGenericOnly(packetId))
            {
                gaps.Add(row);
            }

            if (stats.Count >= 30 && gaps.Count >= 30)
            {
                break;
            }
        }

        return (stats, gaps);
    }

    private static List<DecodeFailureRow> ReadDecodeFailures(SqliteConnection connection)
    {
        var rows = new List<DecodeFailureRow>();
        if (!TableExists(connection, "packet_decode_failures"))
        {
            return rows;
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT packet_id, packet_name, reason, error, COUNT(*) AS count
                              FROM packet_decode_failures
                              GROUP BY packet_id, packet_name, reason, error
                              ORDER BY count DESC
                              LIMIT 30
                              """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new DecodeFailureRow
            {
                PacketId = Text(reader, "packet_id"),
                PacketName = Text(reader, "packet_name"),
                Reason = Text(reader, "reason"),
                Error = Text(reader, "error"),
                Count = Int(reader, "count")
            });
        }

        return rows;
    }
}
