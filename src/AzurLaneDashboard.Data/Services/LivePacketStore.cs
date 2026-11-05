using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class LivePacketStore : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly LivePacketStoreOptions options;
    private readonly CurrentStateWriter currentStateWriter;
    private readonly RecentKeySet seenPayloadKeys;
    private readonly RecentKeySet seenDecodedKeys;
    private readonly RecentKeySet seenFailureKeys;
    private readonly Dictionary<string, int> packetIdCounts = new(StringComparer.Ordinal);
    private int packetsSeen;
    private int knownPacketsSeen;
    private int knownPacketsDecoded;
    private int packetPayloadsSaved;
    private int decodeFailuresSaved;
    private int lastSummaryPacketsSeen;
    private int lastRetentionSweepPacketsSeen = -1;
    private bool summaryDirty;
    private StateApplyResult? lastStateApplyResult;

    /// <summary>
    /// Raised on the capture thread after a decoded packet changed the current state and was committed,
    /// so a viewer can reload at once instead of polling the file. Packets with no state effect stay silent.
    /// </summary>
    public event Action<StateApplyResult>? StateApplied;

    public LivePacketStore(string databasePath, LivePacketStoreOptions? options = null)
    {
        this.options = options ?? new LivePacketStoreOptions();
        var dedupeKeyLimit = Math.Max(0, this.options.MaxRecentDedupeKeys);
        seenPayloadKeys = new RecentKeySet(dedupeKeyLimit);
        seenDecodedKeys = new RecentKeySet(dedupeKeyLimit);
        seenFailureKeys = new RecentKeySet(dedupeKeyLimit);
        databasePath = string.IsNullOrWhiteSpace(databasePath)
            ? ProjectLocator.DefaultDatabasePath()
            : Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Cache = SqliteCacheMode.Shared
        }.ToString());
        connection.Open();
        ConfigureConnection();
        EnsureSchema();
        currentStateWriter = new CurrentStateWriter(connection);
        LoadSummary();
        LoadDedupeKeys();
    }

    public void ObservePacket(
        string source,
        string streamId,
        string direction,
        int packetId,
        int index,
        byte[] payload,
        bool knownPacket)
    {
        packetsSeen++;
        if (knownPacket)
        {
            knownPacketsSeen++;
        }

        var packetName = $"{direction}_{packetId}";
        packetIdCounts[packetName] = packetIdCounts.GetValueOrDefault(packetName) + 1;
        summaryDirty = true;
        var savePayload = options.SavePacketPayloads &&
                          seenPayloadKeys.Add(PayloadKey(source, streamId, direction, packetId, index,
                              PayloadFingerprint(payload)));
        if (!savePayload && knownPacket && !RetentionSweepDue())
        {
            return;
        }

        if (!savePayload && !RetentionSweepDue() && !SummaryWriteDue())
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        if (savePayload)
        {
            var payloadHex = PayloadHex(payload);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                                  INSERT INTO packet_payloads (source, stream_id, direction, packet_id, packet_name, "index", payload_hex, seen_at)
                                  VALUES ($source, $stream, $direction, $packetId, $packetName, $index, $payloadHex, $seenAt)
                                  """;
            AddPacketParameters(command, source, streamId, direction, packetId, packetName, index);
            command.Parameters.AddWithValue("$payloadHex", payloadHex);
            command.Parameters.AddWithValue("$seenAt", NowText());
            command.ExecuteNonQuery();
            packetPayloadsSaved++;
        }

        SweepRetentionIfNeeded(transaction);
        WriteSummaryIfNeeded(transaction);
        transaction.Commit();
    }

    public bool AppendDecoded(
        string source,
        string streamId,
        string direction,
        int packetId,
        int index,
        string json)
    {
        var key = DecodedKey(streamId, direction, packetId, index, json);
        if (!seenDecodedKeys.Add(key))
        {
            return false;
        }

        knownPacketsDecoded++;
        summaryDirty = true;
        using var document = JsonDocument.Parse(json);
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                                  INSERT INTO decoded_packets (source, stream_id, direction, packet_id, packet_name, "index", json)
                                  VALUES ($source, $stream, $direction, $packetId, $packetName, $index, $json)
                                  """;
            AddPacketParameters(command, source, streamId, direction, packetId, $"{direction}_{packetId}", index);
            command.Parameters.AddWithValue("$json", json);
            command.ExecuteNonQuery();
        }

        var decodedRowId = LastInsertRowId(transaction);
        var stateResult = currentStateWriter.ApplyDecodedPacket(
            transaction,
            new DecodedPacketEnvelope(decodedRowId, source, streamId, direction, packetId, index),
            document.RootElement);
        lastStateApplyResult = stateResult;

        SweepRetentionIfNeeded(transaction);
        WriteSummaryIfNeeded(transaction);
        transaction.Commit();
        if (stateResult.Status == "Applied")
        {
            StateApplied?.Invoke(stateResult);
        }

        return true;
    }

    public bool AppendDecodeFailure(
        string source,
        string streamId,
        string direction,
        int packetId,
        int index,
        byte[] payload,
        string reason,
        string error)
    {
        var key = FailureKey(
            source,
            streamId,
            direction,
            packetId,
            index,
            reason,
            options.SaveDecodeFailurePayloads ? PayloadFingerprint(payload) : "");
        if (!seenFailureKeys.Add(key))
        {
            return false;
        }

        var payloadHex = options.SaveDecodeFailurePayloads ? PayloadHex(payload) : "";
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              INSERT INTO packet_decode_failures (source, stream_id, direction, packet_id, packet_name, "index", reason, error, payload_hex, seen_at)
                              VALUES ($source, $stream, $direction, $packetId, $packetName, $index, $reason, $error, $payloadHex, $seenAt)
                              """;
        AddPacketParameters(command, source, streamId, direction, packetId, $"{direction}_{packetId}", index);
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$error", error.Length > 1000 ? error[..1000] : error);
        command.Parameters.AddWithValue("$payloadHex", payloadHex);
        command.Parameters.AddWithValue("$seenAt", NowText());
        command.ExecuteNonQuery();
        decodeFailuresSaved++;
        summaryDirty = true;
        SweepRetentionIfNeeded(transaction);
        WriteSummaryIfNeeded(transaction);
        transaction.Commit();
        return true;
    }

    public void Dispose()
    {
        try
        {
            if (summaryDirty)
            {
                using var transaction = connection.BeginTransaction();
                WriteSummary(transaction);
                transaction.Commit();
            }
        }
        finally
        {
            connection.Dispose();
        }
    }
}
