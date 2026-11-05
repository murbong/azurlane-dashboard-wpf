using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static readonly ConditionalWeakTable<SqliteConnection, HashSet<string>> TableNameCache = new();
    private static string SnapshotSignature(string databasePath)
    {
        return string.Join("|", new[]
        {
            SnapshotFileSignature(databasePath),
            SnapshotFileSignature(databasePath + "-wal"),
            SnapshotFileSignature(databasePath + "-shm")
        });
    }

    private static string SnapshotFileSignature(string path)
    {
        var info = new FileInfo(path);
        return info.Exists
            ? $"{info.FullName}:{info.Length}:{info.LastWriteTimeUtc.Ticks}"
            : $"{Path.GetFullPath(path)}:missing";
    }

    private static void ConfigureReadConnection(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              PRAGMA busy_timeout=5000;
                              PRAGMA query_only=ON;
                              PRAGMA temp_store=MEMORY;
                              PRAGMA cache_size=-65536;
                              PRAGMA mmap_size=268435456;
                              """;
        command.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        return TableNameCache.GetValue(connection, ReadTableNames).Contains(tableName);
    }

    private static HashSet<string> ReadTableNames(SqliteConnection connection)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static int CountRows(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{tableName}\"";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static int MaxRowId(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COALESCE(MAX(rowid), 0) FROM \"{tableName}\"";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string Text(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? ""
            : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? "";
    }

    private static int Int(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal))
        {
            return 0;
        }

        return ToInt(reader.GetValue(ordinal));
    }

    private static JsonElement MapObject(JsonElement map, int key)
    {
        return key > 0 && map.ValueKind == JsonValueKind.Object &&
               map.TryGetProperty(key.ToString(CultureInfo.InvariantCulture), out var value)
            ? value
            : default;
    }

    private static bool HasMapKey(JsonElement map, int key)
    {
        return key > 0 && map.ValueKind == JsonValueKind.Object &&
               map.TryGetProperty(key.ToString(CultureInfo.InvariantCulture), out _);
    }

    private static JsonElement Child(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value)
            ? value
            : default;
    }

    private static IEnumerable<JsonElement> Array(JsonElement element, string propertyName)
    {
        var value = Child(element, propertyName);
        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : Enumerable.Empty<JsonElement>();
    }

    // A repeated field the schema decoder wrote as a single value because it had one element.
    private static IEnumerable<JsonElement> Items(JsonElement element, string propertyName)
    {
        var value = Child(element, propertyName);
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray(),
            JsonValueKind.Undefined or JsonValueKind.Null => Enumerable.Empty<JsonElement>(),
            _ => new[] { value }
        };
    }

    private static IEnumerable<JsonElement> ArrayItems(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : Enumerable.Empty<JsonElement>();
    }

    private static bool HasProperties(JsonElement element, params string[] propertyNames)
    {
        return element.ValueKind == JsonValueKind.Object &&
               propertyNames.All(name => element.TryGetProperty(name, out _));
    }

    private static JsonElement ParseJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string JsonString(JsonElement element, string propertyName)
    {
        return ValueText(Child(element, propertyName));
    }

    private static int JsonInt(JsonElement element, string propertyName)
    {
        return IntValue(Child(element, propertyName));
    }

    private static string ValueText(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.TryGetInt64(out var integer)
                ? integer.ToString(CultureInfo.InvariantCulture)
                : value.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        };
    }

    private static int IntValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetInt32(out var integer) ? integer : (int)Math.Round(value.GetDouble());
        }

        return ToInt(ValueText(value));
    }

    private static int ToInt(object? value)
    {
        return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static int FirstPositive(params int[] values) => values.FirstOrDefault(value => value > 0);

    private static int CeilingDiv(int numerator, int denominator) =>
        denominator <= 0 ? 0 : (numerator + denominator - 1) / denominator;

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static string CustomNameFromDisplayName(string displayName, string baseName)
    {
        displayName = displayName.Trim();
        baseName = baseName.Trim();
        var prefix = $"{baseName} (";
        if (displayName.Length <= prefix.Length ||
            !displayName.StartsWith(prefix, StringComparison.Ordinal) ||
            displayName[^1] != ')')
        {
            return "";
        }

        return displayName[prefix.Length..^1].Trim();
    }

    private static string Asset(string value, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Trim().Replace('\\', '/');
    }
}
