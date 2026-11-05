using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class DatabaseMaintenanceService
{
    private const int ReplaceRetryCount = 8;
    private const int ReplaceRetryDelayMilliseconds = 250;

    public IReadOnlyList<DatabaseTableStat> Analyze(string databasePath)
    {
        using var connection = OpenReadOnly(databasePath);
        return Analyze(connection);
    }

    public DatabaseMaintenanceResult Compact(string databasePath, DatabaseMaintenanceOptions? options = null)
    {
        options ??= new DatabaseMaintenanceOptions();
        databasePath = Path.GetFullPath(databasePath);
        using var connection = Open(databasePath);
        return CompactOpenDatabase(databasePath, connection, options, null);
    }

    public DatabaseMaintenanceResult DeepCompact(string databasePath, DatabaseMaintenanceOptions? options = null)
    {
        options ??= new DatabaseMaintenanceOptions();
        databasePath = Path.GetFullPath(databasePath);
        var compactedPath = BuildMaintenancePath(databasePath, "vacuum.sqlite");
        var backupPath = BuildMaintenancePath(databasePath, "replace-backup.sqlite");
        var replaceSucceeded = false;

        try
        {
            DatabaseMaintenanceResult result;
            using (var connection = Open(databasePath, pooling: false))
            {
                result = CompactOpenDatabase(databasePath, connection, options, compactedPath);
            }

            SqliteConnection.ClearAllPools();
            ReplaceDatabaseFile(databasePath, compactedPath, backupPath);
            replaceSucceeded = true;
            var sizeAfter = File.Exists(databasePath) ? new FileInfo(databasePath).Length : 0;
            return result with { SizeAfterBytes = sizeAfter };
        }
        finally
        {
            if (replaceSucceeded)
            {
                TryDeleteFile(compactedPath);
                TryDeleteFile(backupPath);
            }
        }
    }

    private static DatabaseMaintenanceResult CompactOpenDatabase(
        string databasePath,
        SqliteConnection connection,
        DatabaseMaintenanceOptions options,
        string? vacuumIntoPath)
    {
        var sizeBefore = File.Exists(databasePath) ? new FileInfo(databasePath).Length : 0;
        var beforeStats = Analyze(connection);

        var deletedPayloadRows = 0;
        var deletedFailureRows = 0;
        var deletedDecodedRows = 0;

        using (var transaction = connection.BeginTransaction())
        {
            if (TableExists(connection, "packet_payloads", transaction))
            {
                if (options.ClearPacketPayloads)
                {
                    deletedPayloadRows = ExecuteDelete(connection, transaction, "DELETE FROM packet_payloads");
                }
                else if (options.KeepRecentPacketPayloadRows > 0)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = """
                        DELETE FROM packet_payloads
                        WHERE rowid < COALESCE((
                            SELECT MIN(rowid)
                            FROM (
                                SELECT rowid FROM packet_payloads ORDER BY rowid DESC LIMIT $limit
                            )
                        ), -1)
                        """;
                    command.Parameters.AddWithValue("$limit", options.KeepRecentPacketPayloadRows);
                    deletedPayloadRows = command.ExecuteNonQuery();
                }
            }

            if (TableExists(connection, "packet_decode_failures", transaction) && options.KeepDecodeFailuresPerPacket >= 0)
            {
                if (options.KeepDecodeFailuresPerPacket == 0)
                {
                    deletedFailureRows = ExecuteDelete(connection, transaction, "DELETE FROM packet_decode_failures");
                }
                else
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = """
                        DELETE FROM packet_decode_failures
                        WHERE rowid IN (
                            SELECT rowid FROM (
                                SELECT rowid,
                                       ROW_NUMBER() OVER (
                                           PARTITION BY packet_id, packet_name, reason
                                           ORDER BY rowid DESC
                                       ) AS rn
                                FROM packet_decode_failures
                            )
                            WHERE rn > $keep
                        )
                        """;
                    command.Parameters.AddWithValue("$keep", options.KeepDecodeFailuresPerPacket);
                    deletedFailureRows = command.ExecuteNonQuery();
                }

                if (options.ClearDecodeFailurePayloads)
                {
                    ExecuteDelete(
                        connection,
                        transaction,
                        "UPDATE packet_decode_failures SET payload_hex = '' WHERE length(payload_hex) > 0");
                }
            }

            if (TableExists(connection, "decoded_packets", transaction) && options.KeepRecentDecodedRows > 0)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    DELETE FROM decoded_packets
                    WHERE rowid < COALESCE((
                        SELECT MIN(rowid)
                        FROM (
                            SELECT rowid FROM decoded_packets ORDER BY rowid DESC LIMIT $limit
                        )
                    ), -1)
                    """;
                command.Parameters.AddWithValue("$limit", options.KeepRecentDecodedRows);
                deletedDecodedRows = command.ExecuteNonQuery();
            }

            UpdateSummaryCounts(connection, transaction);
            transaction.Commit();
        }

        if (!string.IsNullOrWhiteSpace(vacuumIntoPath))
        {
            CheckpointWal(connection);
            TryDeleteFile(vacuumIntoPath);
            using var vacuum = connection.CreateCommand();
            vacuum.CommandText = "VACUUM INTO $target";
            vacuum.Parameters.AddWithValue("$target", vacuumIntoPath);
            vacuum.ExecuteNonQuery();
        }
        else if (options.Vacuum)
        {
            using var vacuum = connection.CreateCommand();
            vacuum.CommandText = "VACUUM";
            vacuum.ExecuteNonQuery();
        }
        else
        {
            using var optimize = connection.CreateCommand();
            optimize.CommandText = "PRAGMA optimize";
            optimize.ExecuteNonQuery();
        }

        var afterStats = Analyze(connection);
        var sizeAfterPath = string.IsNullOrWhiteSpace(vacuumIntoPath) ? databasePath : vacuumIntoPath;
        var sizeAfter = File.Exists(sizeAfterPath) ? new FileInfo(sizeAfterPath).Length : 0;
        return new DatabaseMaintenanceResult(
            sizeBefore,
            sizeAfter,
            deletedPayloadRows,
            deletedFailureRows,
            deletedDecodedRows,
            beforeStats,
            afterStats);
    }

}
