using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class LivePacketStore
{
    private static string Text(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? ""
            : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? "";
    }

    private int CountRows(string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private int CountRows(string table, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private long LastInsertRowId(SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void AddPacketParameters(
        SqliteCommand command,
        string source,
        string streamId,
        string direction,
        int packetId,
        string packetName,
        int index)
    {
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$stream", streamId);
        command.Parameters.AddWithValue("$direction", direction);
        command.Parameters.AddWithValue("$packetId", packetId);
        command.Parameters.AddWithValue("$packetName", packetName);
        command.Parameters.AddWithValue("$index", index);
    }

    private static string PayloadKey(string source, string streamId, string direction, int packetId, int index,
        string payloadHex)
        => string.Join('\u001f', source, streamId, direction, packetId.ToString(CultureInfo.InvariantCulture),
            index.ToString(CultureInfo.InvariantCulture), payloadHex);

    private string PayloadHex(byte[] payload)
    {
        return Convert.ToHexString(LimitedPayload(payload)).ToLowerInvariant();
    }

    private string PayloadFingerprint(byte[] payload)
    {
        return Convert.ToHexString(SHA1.HashData(LimitedPayload(payload))).ToLowerInvariant();
    }

    private ReadOnlySpan<byte> LimitedPayload(byte[] payload)
    {
        return options.MaxPayloadBytes > 0 && payload.Length > options.MaxPayloadBytes
            ? payload.AsSpan(0, options.MaxPayloadBytes)
            : payload.AsSpan();
    }

    private static string DecodedKey(string streamId, string direction, int packetId, int index, string json)
        => string.Join('\u001f', streamId, direction, packetId.ToString(CultureInfo.InvariantCulture),
            index.ToString(CultureInfo.InvariantCulture), Sha1(json));

    private static string FailureKey(string source, string streamId, string direction, int packetId, int index,
        string reason, string payloadHex)
        => string.Join('\u001f', source, streamId, direction, packetId.ToString(CultureInfo.InvariantCulture),
            index.ToString(CultureInfo.InvariantCulture), reason, payloadHex);

    private static string Sha1(string value)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NowText() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static int JsonInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result) ? result : 0;
    }

    private static int ToInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }


    private sealed class RecentKeySet(int capacity)
    {
        private readonly Queue<string> order = new();
        private readonly HashSet<string> keys = new(StringComparer.Ordinal);

        public bool Add(string key)
        {
            if (capacity <= 0)
            {
                return true;
            }

            if (!keys.Add(key))
            {
                return false;
            }

            order.Enqueue(key);
            while (order.Count > capacity && order.TryDequeue(out var oldest))
            {
                keys.Remove(oldest);
            }

            return true;
        }
    }
}
