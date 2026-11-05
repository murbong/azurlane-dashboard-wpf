using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class CurrentStateWriter
{
    private void StorePendingRequest(SqliteTransaction tx, DecodedPacketEnvelope packet, int responsePacketId,
        string area, JsonElement json)
    {
        Execute(
            tx,
            """
            INSERT INTO current_pending_requests (
                pending_key, request_packet_id, response_packet_id, area, source, stream_id,
                packet_index, decoded_rowid, created_at, request_json)
            VALUES ($key, $request, $response, $area, $source, $stream, $index, $rowid, $now, $json)
            ON CONFLICT(pending_key) DO UPDATE SET
                request_packet_id = excluded.request_packet_id,
                area = excluded.area,
                packet_index = excluded.packet_index,
                decoded_rowid = excluded.decoded_rowid,
                created_at = excluded.created_at,
                request_json = excluded.request_json
            """,
            ("$key", PendingKey(packet, responsePacketId)),
            ("$request", packet.PacketId),
            ("$response", responsePacketId),
            ("$area", area),
            ("$source", packet.Source),
            ("$stream", packet.StreamId),
            ("$index", packet.Index),
            ("$rowid", packet.DecodedRowId),
            ("$now", NowText()),
            ("$json", Json(json)));
    }

    private JsonElement? TakePendingRequest(SqliteTransaction tx, DecodedPacketEnvelope packet, int responsePacketId)
    {
        var keyValue = PendingKey(packet, responsePacketId);
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText =
            "SELECT pending_key, request_json FROM current_pending_requests WHERE pending_key = $key LIMIT 1";
        command.Parameters.AddWithValue("$key", keyValue);
        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return TakePendingFromReader(tx, reader);
        }

        reader.Close();
        using var fallback = connection.CreateCommand();
        fallback.Transaction = tx;
        fallback.CommandText = """
                               SELECT pending_key, request_json
                               FROM current_pending_requests
                               WHERE response_packet_id = $response
                               ORDER BY decoded_rowid DESC
                               LIMIT 1
                               """;
        fallback.Parameters.AddWithValue("$response", responsePacketId);
        using var fallbackReader = fallback.ExecuteReader();
        return fallbackReader.Read() ? TakePendingFromReader(tx, fallbackReader) : null;
    }

    private JsonElement TakePendingFromReader(SqliteTransaction tx, SqliteDataReader reader)
    {
        var key = reader.GetString(0);
        var raw = reader.GetString(1);
        reader.Close();
        Execute(tx, "DELETE FROM current_pending_requests WHERE pending_key = $key", ("$key", key));
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    private void UpsertSystemStatus(SqliteTransaction tx, string key, string detail,
        IReadOnlyDictionary<string, object?> extra)
    {
        var raw = new Dictionary<string, object?>
        {
            ["status"] = "incremental",
            ["status_label"] = "incremental",
            ["status_kind"] = "good",
            ["detail"] = detail
        };
        foreach (var item in extra)
        {
            raw[item.Key] = item.Value;
        }

        Execute(
            tx,
            """
            INSERT INTO current_system_statuses (system_key, status, status_label, status_kind, detail, raw_json)
            VALUES ($key, 'incremental', 'incremental', 'good', $detail, $raw)
            ON CONFLICT(system_key) DO UPDATE SET
                status = excluded.status,
                status_label = excluded.status_label,
                status_kind = excluded.status_kind,
                detail = excluded.detail,
                raw_json = excluded.raw_json
            """,
            ("$key", key),
            ("$detail", detail),
            ("$raw", Json(raw)));
    }

    private void UpdateMeta(SqliteTransaction tx, StateApplyResult result)
    {
        Execute(
            tx,
            """
            INSERT INTO current_state_meta (id, schema_version, mode, last_decoded_rowid, updated_at, last_error)
            VALUES (1, $version, 'incremental', $rowid, $now, $error)
            ON CONFLICT(id) DO UPDATE SET
                schema_version = excluded.schema_version,
                mode = excluded.mode,
                last_decoded_rowid = MAX(current_state_meta.last_decoded_rowid, excluded.last_decoded_rowid),
                updated_at = excluded.updated_at,
                last_error = excluded.last_error
            """,
            ("$version", SchemaVersion),
            ("$rowid", result.LastDecodedRowId),
            ("$now", NowText()),
            ("$error", result.Status == "Failed" ? result.Detail : ""));
    }

    private void RecordApplyLog(SqliteTransaction tx, DecodedPacketEnvelope packet, StateApplyResult result)
    {
        Execute(
            tx,
            """
            INSERT INTO current_packet_apply_log
                (decoded_rowid, packet_id, packet_name, direction, status, area, detail, applied_at)
            VALUES ($rowid, $packet, $name, $direction, $status, $area, $detail, $now)
            ON CONFLICT(decoded_rowid) DO UPDATE SET
                status = excluded.status,
                area = excluded.area,
                detail = excluded.detail,
                applied_at = excluded.applied_at
            """,
            ("$rowid", result.LastDecodedRowId),
            ("$packet", packet.PacketId),
            ("$name", $"{packet.Direction}_{packet.PacketId.ToString(CultureInfo.InvariantCulture)}"),
            ("$direction", packet.Direction),
            ("$status", result.Status),
            ("$area", result.Area),
            ("$detail", result.Detail),
            ("$now", NowText()));
    }

    private static Dictionary<string, object?> ShipRaw(string uid, long rowId, JsonElement ship)
    {
        var customName = FirstNonEmpty(Text(ship, "custom_name"), Text(ship, "name"));
        var raw = JsonDictionary(ship);
        raw["ship_uid"] = uid;
        raw["ship_template_id"] = Int(ship, "template_id");
        raw["custom_name"] = customName;
        raw["display_name"] = FirstNonEmpty(customName, uid);
        raw["limit_break"] = FirstPositive(Int(ship, "breakout"), LimitBreakFromTemplate(Int(ship, "template_id")));
        raw["locked"] = Int(ship, "is_locked") != 0;
        raw["snapshot_rowid"] = rowId;
        return raw;
    }

    private static int LimitBreakFromTemplate(int templateId) =>
        templateId > 0 ? Math.Clamp(templateId % 10 - 1, 0, 3) : 0;

    private static string PendingKey(DecodedPacketEnvelope packet, int responsePacketId) =>
        $"{packet.Source}\u001f{packet.StreamId}\u001f{responsePacketId.ToString(CultureInfo.InvariantCulture)}";

    private void Execute(SqliteTransaction tx, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    // A repeated field that captures decoded before the schema fix wrote as a single value when it had one element.
    private static IEnumerable<JsonElement> Items(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(property, out var value)) return [];
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray(),
            JsonValueKind.Null or JsonValueKind.Undefined => [],
            _ => [value]
        };
    }

    private static IEnumerable<JsonElement> Array(JsonElement root, string property)
    {
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];
    }

    private static JsonElement ObjectOrEmpty(JsonElement root, string property)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static int Int(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(property, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var number) => number,
            JsonValueKind.True => 1,
            _ => 0
        };
    }

    private static int ToInt(object? value)
    {
        if (value is null || value == DBNull.Value)
        {
            return 0;
        }

        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static string Text(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(property, out var value))
        {
            return "";
        }

        return ValueText(value);
    }

    private static string ValueText(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "1",
            JsonValueKind.False => "0",
            _ => ""
        };
    }

    private static int FirstPositive(params int[] values) => values.FirstOrDefault(value => value > 0);

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static string Json(JsonElement value) => JsonSerializer.Serialize(value);

    private static string Json(object? value) => JsonSerializer.Serialize(value);

    private static Dictionary<string, object?> JsonDictionary(JsonElement element)
    {
        var json = JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText());
        return json ?? new Dictionary<string, object?>();
    }

    private static string NowText() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
