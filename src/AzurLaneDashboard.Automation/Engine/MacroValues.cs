using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AzurLaneDashboard.Automation.Engine;

/// <summary>
/// Macro values as the Python runtime sees them: numbers (long or double), strings, booleans, null and nested
/// JSON. Comparisons follow its rules, except that ordering with a missing side is false instead of an error.
/// </summary>
public static class MacroValues
{
    public static object? FromJson(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value => FromJsonValue(value),
        _ => node.DeepClone()
    };

    public static object? FromJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.TryGetInt64(out var whole) ? whole : element.GetDouble(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => JsonNode.Parse(element.GetRawText())
    };

    private static object? FromJsonValue(JsonValue value)
    {
        switch (value.GetValueKind())
        {
            case JsonValueKind.Number:
                if (value.TryGetValue<long>(out var whole)) return whole;
                if (value.TryGetValue<int>(out var small)) return (long)small;
                if (value.TryGetValue<double>(out var real)) return real;
                return double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture);
            case JsonValueKind.String:
                return value.GetValue<string>();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }

    public static bool Truthy(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        long number => number != 0,
        int number => number != 0,
        double number => number != 0,
        string text => text.Length > 0,
        JsonArray list => list.Count > 0,
        JsonObject map => map.Count > 0,
        _ => true
    };

    public static bool AreEqual(object? left, object? right)
    {
        if (left is null || right is null) return left is null && right is null;
        if (IsNumber(left) && IsNumber(right)) return ToDouble(left) == ToDouble(right);
        if (left is bool a && right is bool b) return a == b;
        if (left is string x && right is string y) return string.Equals(x, y, StringComparison.Ordinal);
        if (left is JsonNode n && right is JsonNode m) return JsonNode.DeepEquals(n, m);
        return false;
    }

    public static bool Compare(object? left, string op, object? right)
    {
        switch (op)
        {
            case "equals":
                return AreEqual(left, right);
            case "not_equals":
                return !AreEqual(left, right);
            case "gt" or "gte" or "lt" or "lte":
            {
                // A missing value on either side (no game state yet, an unset variable) never orders.
                int order;
                if (IsNumber(left) && IsNumber(right)) order = ToDouble(left!).CompareTo(ToDouble(right!));
                else if (left is string x && right is string y) order = string.CompareOrdinal(x, y);
                else return false;
                return op switch { "gt" => order > 0, "gte" => order >= 0, "lt" => order < 0, _ => order <= 0 };
            }
            default:
                return Truthy(left);
        }
    }

    public static bool IsNumber(object? value) => value is long or int or double;

    public static double ToDouble(object value) => value switch
    {
        long number => number,
        int number => number,
        double number => number,
        _ => 0
    };

    public static long ToLong(object? value) => value switch
    {
        long number => number,
        int number => number,
        double number => (long)number,
        string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        bool flag => flag ? 1 : 0,
        _ => 0
    };

    public static string Format(object? value) => value switch
    {
        null => "-",
        bool flag => flag ? "true" : "false",
        double number => number.ToString("0.###", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        JsonNode node => node.ToJsonString(),
        _ => value.ToString() ?? ""
    };
}
