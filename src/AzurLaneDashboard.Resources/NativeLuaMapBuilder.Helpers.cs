using System.Globalization;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    private static void WriteJson(string path, object data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
    }

    private static void WriteJsonElement(string path, JsonElement element)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(element, JsonOptions));
    }

    private static object? Get(IReadOnlyDictionary<string, object?>? source, string key)
    {
        return source is not null && source.TryGetValue(key, out var value) ? value : null;
    }

    private static object? FirstNonEmpty(params object?[] values)
    {
        return values.FirstOrDefault(HasValue);
    }

    private static int FirstPositive(params int[] values)
    {
        return values.FirstOrDefault(value => value > 0);
    }

    private static bool HasValue(object? value)
    {
        return value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            Array array => array.Length > 0,
            _ => true
        };
    }

    private static int SafeInt(object? value, int defaultValue = 0)
    {
        return value switch
        {
            null => defaultValue,
            int intValue => intValue,
            long longValue => checked((int)longValue),
            double doubleValue => (int)doubleValue,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) =>
                parsed,
            _ => defaultValue
        };
    }

    private static int[] IntArray(object? value)
    {
        return value switch
        {
            int[] array => array,
            IEnumerable<int> items => items.ToArray(),
            IEnumerable<object?> items => items.Select(SafeInt).Where(item => item != 0).ToArray(),
            _ => []
        };
    }
}