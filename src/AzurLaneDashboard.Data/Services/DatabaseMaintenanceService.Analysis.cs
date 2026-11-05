using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class DatabaseMaintenanceService
{
    private static IReadOnlyList<DatabaseTableStat> Analyze(SqliteConnection connection)
    {
        var stats = new List<DatabaseTableStat>();
        foreach (var table in new[] { "packet_payloads", "packet_decode_failures", "decoded_packets", "json_store", "ships", "equipment", "equipment_items", "meta" })
        {
            if (!TableExists(connection, table, null))
            {
                continue;
            }

            var rows = ScalarLong(connection, $"SELECT COUNT(*) FROM \"{table}\"");
            var textBytes = table switch
            {
                "packet_payloads" => ScalarLong(connection, "SELECT COALESCE(SUM(length(payload_hex)),0) FROM packet_payloads"),
                "packet_decode_failures" => ScalarLong(connection, "SELECT COALESCE(SUM(length(payload_hex)),0) + COALESCE(SUM(length(error)),0) FROM packet_decode_failures"),
                "decoded_packets" => ScalarLong(connection, "SELECT COALESCE(SUM(length(json)),0) FROM decoded_packets"),
                "json_store" => ScalarLong(connection, "SELECT COALESCE(SUM(length(json)),0) FROM json_store"),
                _ => 0
            };
            stats.Add(new DatabaseTableStat(table, rows, textBytes));
        }

        return stats;
    }

    private static void UpdateSummaryCounts(SqliteConnection connection, SqliteTransaction transaction)
    {
        if (!TableExists(connection, "meta", transaction))
        {
            return;
        }

        var payloadRows = TableExists(connection, "packet_payloads", transaction)
            ? ScalarLong(connection, "SELECT COUNT(*) FROM packet_payloads", transaction)
            : 0;
        var failureRows = TableExists(connection, "packet_decode_failures", transaction)
            ? ScalarLong(connection, "SELECT COUNT(*) FROM packet_decode_failures", transaction)
            : 0;
        var decodedRows = TableExists(connection, "decoded_packets", transaction)
            ? ScalarLong(connection, "SELECT COUNT(*) FROM decoded_packets", transaction)
            : 0;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE meta
            SET value = json_set(
                COALESCE(value, '{}'),
                '$.packet_payloads_saved', $payloadRows,
                '$.decode_failures_saved', $failureRows,
                '$.known_packets_decoded', $decodedRows
            )
            WHERE key = 'summary_json'
            """;
        command.Parameters.AddWithValue("$payloadRows", payloadRows);
        command.Parameters.AddWithValue("$failureRows", failureRows);
        command.Parameters.AddWithValue("$decodedRows", decodedRows);
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string databasePath, bool pooling = true)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Cache = SqliteCacheMode.Shared,
            Pooling = pooling
        }.ToString());
        connection.Open();
        ConfigureConnection(connection);
        return connection;
    }

    // Private cache, as in NativeSnapshotService: a read-only member would make the shared cache read-only.
    private static SqliteConnection OpenReadOnly(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        connection.Open();
        ConfigureConnection(connection);
        return connection;
    }

    private static void ConfigureConnection(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000";
        command.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection connection, string tableName, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1";
        command.Parameters.AddWithValue("$name", tableName);
        return command.ExecuteScalar() is not null;
    }

    private static int ExecuteDelete(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    private static long ScalarLong(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
