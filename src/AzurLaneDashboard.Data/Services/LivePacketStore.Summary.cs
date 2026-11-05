using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class LivePacketStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private void LoadSummary()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = 'summary_json' LIMIT 1";
        var raw = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(raw))
        {
            packetPayloadsSaved = CountRows("packet_payloads");
            decodeFailuresSaved = CountRows("packet_decode_failures");
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            packetsSeen = JsonInt(root, "packets_seen");
            knownPacketsSeen = JsonInt(root, "known_packets_seen");
            knownPacketsDecoded = JsonInt(root, "known_packets_decoded");
            packetPayloadsSaved = JsonInt(root, "packet_payloads_saved");
            if (packetPayloadsSaved <= 0)
            {
                packetPayloadsSaved = CountRows("packet_payloads");
            }

            decodeFailuresSaved = JsonInt(root, "decode_failures_saved");
            if (decodeFailuresSaved <= 0)
            {
                decodeFailuresSaved = CountRows("packet_decode_failures");
            }
            if (root.TryGetProperty("packet_id_counts", out var counts) && counts.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in counts.EnumerateObject())
                {
                    packetIdCounts[property.Name] = property.Value.TryGetInt32(out var count) ? count : 0;
                }
            }
        }
        catch (JsonException)
        {
            packetPayloadsSaved = CountRows("packet_payloads");
            decodeFailuresSaved = CountRows("packet_decode_failures");
        }

        lastSummaryPacketsSeen = packetsSeen;
        summaryDirty = false;
    }

    private void LoadDedupeKeys()
    {
        if (options.MaxRecentDedupeKeys <= 0)
        {
            return;
        }

        if (options.SavePacketPayloads)
        {
            using var payloadCommand = connection.CreateCommand();
            payloadCommand.CommandText = """
                                         SELECT source, stream_id, direction, packet_id, "index"
                                         FROM packet_payloads
                                         ORDER BY rowid DESC
                                         LIMIT $limit
                                         """;
            payloadCommand.Parameters.AddWithValue("$limit", options.MaxRecentDedupeKeys);
            using var payloadReader = payloadCommand.ExecuteReader();
            while (payloadReader.Read())
            {
                seenPayloadKeys.Add(PayloadKey(
                    Text(payloadReader, 0),
                    Text(payloadReader, 1),
                    Text(payloadReader, 2),
                    ToInt(Text(payloadReader, 3)),
                    ToInt(Text(payloadReader, 4)),
                    ""));
            }
        }

        using (var decodedCommand = connection.CreateCommand())
        {
            decodedCommand.CommandText = """
                                         SELECT stream_id, direction, packet_id, "index", json
                                         FROM decoded_packets
                                         ORDER BY rowid DESC
                                         LIMIT $limit
                                         """;
            decodedCommand.Parameters.AddWithValue("$limit", options.MaxRecentDedupeKeys);
            using var decodedReader = decodedCommand.ExecuteReader();
            while (decodedReader.Read())
            {
                seenDecodedKeys.Add(DecodedKey(
                    Text(decodedReader, 0),
                    Text(decodedReader, 1),
                    ToInt(Text(decodedReader, 2)),
                    ToInt(Text(decodedReader, 3)),
                    Text(decodedReader, 4)));
            }
        }

        using (var failureCommand = connection.CreateCommand())
        {
            failureCommand.CommandText = """
                                         SELECT source, stream_id, direction, packet_id, "index", reason
                                         FROM packet_decode_failures
                                         ORDER BY rowid DESC
                                         LIMIT $limit
                                         """;
            failureCommand.Parameters.AddWithValue("$limit", options.MaxRecentDedupeKeys);
            using var failureReader = failureCommand.ExecuteReader();
            while (failureReader.Read())
            {
                seenFailureKeys.Add(FailureKey(
                    Text(failureReader, 0),
                    Text(failureReader, 1),
                    Text(failureReader, 2),
                    ToInt(Text(failureReader, 3)),
                    ToInt(Text(failureReader, 4)),
                    Text(failureReader, 5),
                    ""));
            }
        }
    }

    private void SweepRetentionIfNeeded(SqliteTransaction transaction)
    {
        if (!RetentionSweepDue())
        {
            return;
        }

        lastRetentionSweepPacketsSeen = packetsSeen;

        if (options.MaxPacketPayloadRows > 0)
        {
            using var payloadCommand = connection.CreateCommand();
            payloadCommand.Transaction = transaction;
            payloadCommand.CommandText = """
                                         DELETE FROM packet_payloads
                                         WHERE rowid < COALESCE((
                                             SELECT MIN(rowid)
                                             FROM (
                                                 SELECT rowid FROM packet_payloads ORDER BY rowid DESC LIMIT $limit
                                             )
                                         ), -1)
                                         """;
            payloadCommand.Parameters.AddWithValue("$limit", options.MaxPacketPayloadRows);
            payloadCommand.ExecuteNonQuery();
            packetPayloadsSaved = CountRows("packet_payloads", transaction);
        }

        if (options.MaxDecodeFailureRowsPerPacket > 0)
        {
            using var failureCommand = connection.CreateCommand();
            failureCommand.Transaction = transaction;
            failureCommand.CommandText = """
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
            failureCommand.Parameters.AddWithValue("$keep", options.MaxDecodeFailureRowsPerPacket);
            failureCommand.ExecuteNonQuery();
            decodeFailuresSaved = CountRows("packet_decode_failures", transaction);
        }

        if (options.MaxDecodedPacketRows > 0)
        {
            using var decodedCommand = connection.CreateCommand();
            decodedCommand.Transaction = transaction;
            decodedCommand.CommandText = """
                                         DELETE FROM decoded_packets
                                         WHERE rowid < COALESCE((
                                             SELECT MIN(rowid)
                                             FROM (
                                                 SELECT rowid FROM decoded_packets ORDER BY rowid DESC LIMIT $limit
                                             )
                                         ), -1)
                                         """;
            decodedCommand.Parameters.AddWithValue("$limit", options.MaxDecodedPacketRows);
            decodedCommand.ExecuteNonQuery();
            knownPacketsDecoded = CountRows("decoded_packets", transaction);
        }
    }

    private void WriteSummary(SqliteTransaction transaction)
    {
        var summary = new Dictionary<string, object?>
        {
            ["packets_seen"] = packetsSeen,
            ["known_packets_seen"] = knownPacketsSeen,
            ["known_packets_decoded"] = knownPacketsDecoded,
            ["inventory_packets_seen"] = knownPacketsSeen,
            ["inventory_packets_decoded"] = knownPacketsDecoded,
            ["packet_payloads_saved"] = packetPayloadsSaved,
            ["decode_failures_saved"] = decodeFailuresSaved,
            ["current_state_mode"] = "incremental",
            ["current_state_last_decoded_rowid"] = lastStateApplyResult?.LastDecodedRowId ?? 0,
            ["current_state_last_status"] = lastStateApplyResult?.Status ?? "",
            ["current_state_last_area"] = lastStateApplyResult?.Area ?? "",
            ["current_state_last_detail"] = lastStateApplyResult?.Detail ?? "",
            ["packet_id_counts"] = packetIdCounts.OrderBy(item => item.Key)
                .ToDictionary(item => item.Key, item => item.Value),
            ["live_updated_at"] = NowText()
        };
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              INSERT INTO meta (key, value) VALUES ('summary_json', $value)
                              ON CONFLICT(key) DO UPDATE SET value = excluded.value
                              """;
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(summary, JsonOptions));
        command.ExecuteNonQuery();
        lastSummaryPacketsSeen = packetsSeen;
        summaryDirty = false;
    }

    private void WriteSummaryIfNeeded(SqliteTransaction transaction)
    {
        if (SummaryWriteDue())
        {
            WriteSummary(transaction);
        }
    }

    private bool SummaryWriteDue()
    {
        var interval = Math.Max(1, options.SummaryWriteIntervalPackets);
        return summaryDirty && packetsSeen - lastSummaryPacketsSeen >= interval;
    }

    private bool RetentionSweepDue()
    {
        return options.RetentionSweepIntervalPackets > 0 &&
               packetsSeen % options.RetentionSweepIntervalPackets == 0 &&
               lastRetentionSweepPacketsSeen != packetsSeen;
    }
}
