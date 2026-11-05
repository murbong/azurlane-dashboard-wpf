using System.Text.Json.Nodes;

namespace AzurLaneDashboard.Protocol;

public sealed partial class GamePayloadDecoder
{
    private static JsonObject Result(byte[] payload) => Fields(payload, new Dictionary<int, string> { [1] = "result" });

    private static JsonObject Fields(byte[] raw, IReadOnlyDictionary<int, string> names) =>
        ProtobufReader.DecodeUInt32Fields(raw, names);

    private static void AddPackedOrSingle(JsonObject target, string propertyName, ProtoField field) =>
        ProtobufReader.AddPackedOrSingle(target, propertyName, field);

    private static void AddPackedValues(JsonObject target, string propertyName, byte[] raw)
    {
        foreach (var value in ProtobufReader.DecodePackedUInt32(raw))
        {
            target.Array(propertyName).Add(value);
        }
    }
}

internal static class JsonObjectExtensions
{
    public static JsonArray Array(this JsonObject target, string propertyName)
    {
        if (target[propertyName] is JsonArray array)
        {
            return array;
        }

        array = new JsonArray();
        target[propertyName] = array;
        return array;
    }

    public static void AddArrayItem(this JsonObject target, string propertyName, JsonNode? value)
    {
        target.Array(propertyName).Add(value);
    }

    public static void AddArrayItem(this JsonObject target, string propertyName, long value)
    {
        target.Array(propertyName).Add(value);
    }
}