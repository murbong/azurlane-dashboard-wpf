using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

namespace AzurLaneDashboard.Protocol;

internal enum ProtoSchemaFieldKind
{
    UInt32,
    UInt64,
    Int32,
    Bool,
    String,
    Message
}

internal sealed record ProtoFieldSchema(
    int Number,
    string Name,
    ProtoSchemaFieldKind Kind,
    bool Repeated,
    string? MessageType);

internal sealed record ProtoMessageSchema(string Name, IReadOnlyDictionary<int, ProtoFieldSchema> Fields);

internal static class ProtobufSchemaDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static JsonObject? Decode(int packetId, byte[] payload)
    {
        return KnownPacketSchemas.TryGetMessageForPacket(packetId, out var messageName)
            ? DecodeMessage(payload, messageName, 0)
            : null;
    }

    private static JsonObject DecodeMessage(byte[] payload, string messageName, int depth)
    {
        if (depth > 16 || !KnownPacketSchemas.TryGetMessage(messageName, out var schema))
        {
            return ProtobufReader.DecodeGeneric(payload, depth);
        }

        var result = new JsonObject();
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (!schema.Fields.TryGetValue(field.Number, out var fieldSchema))
            {
                ProtobufReader.AddRepeated(result, $"field_{field.Number}", DecodeUnknownField(field, depth));
                continue;
            }

            foreach (var value in DecodeField(field, fieldSchema, depth))
            {
                // A repeated field stays a list even with one element, so readers need not guess its shape.
                if (fieldSchema.Repeated)
                {
                    if (result[fieldSchema.Name] is not JsonArray list) result[fieldSchema.Name] = list = [];
                    list.Add(value ?? JsonValue.Create(""));
                }
                else
                {
                    ProtobufReader.AddRepeated(result, fieldSchema.Name, value);
                }
            }
        }

        return result;
    }

    private static IEnumerable<JsonNode?> DecodeField(ProtoField field, ProtoFieldSchema schema, int depth)
    {
        if (schema.Repeated &&
            field.WireType == ProtoWireType.LengthDelimited &&
            schema.Kind is ProtoSchemaFieldKind.UInt32 or ProtoSchemaFieldKind.UInt64 or ProtoSchemaFieldKind.Int32
                or ProtoSchemaFieldKind.Bool)
        {
            foreach (var value in DecodePackedVarints(field.Bytes, schema.Kind))
            {
                yield return value;
            }

            yield break;
        }

        yield return DecodeSingleField(field, schema, depth);
    }

    private static JsonNode? DecodeSingleField(ProtoField field, ProtoFieldSchema schema, int depth)
    {
        return schema.Kind switch
        {
            ProtoSchemaFieldKind.UInt32 when field.WireType == ProtoWireType.Varint =>
                JsonValue.Create(checked((long)field.Varint)),
            ProtoSchemaFieldKind.UInt64 when field.WireType == ProtoWireType.Varint =>
                JsonValue.Create(field.Varint.ToString()),
            ProtoSchemaFieldKind.Int32 when field.WireType == ProtoWireType.Varint =>
                JsonValue.Create(unchecked((int)field.Varint)),
            ProtoSchemaFieldKind.Bool when field.WireType == ProtoWireType.Varint =>
                JsonValue.Create(field.Varint != 0),
            ProtoSchemaFieldKind.String when field.WireType == ProtoWireType.LengthDelimited =>
                JsonValue.Create(ProtobufReader.DecodeString(field.Bytes)),
            ProtoSchemaFieldKind.Message when field.WireType == ProtoWireType.LengthDelimited =>
                DecodeNestedMessage(field.Bytes, schema.MessageType, depth + 1),
            _ => DecodeUnknownField(field, depth)
        };
    }

    private static IEnumerable<JsonNode?> DecodePackedVarints(byte[] payload, ProtoSchemaFieldKind kind)
    {
        var values = new List<JsonNode?>();
        var span = payload.AsSpan();
        var position = 0;
        while (position < span.Length)
        {
            var value = ProtobufReader.ReadVarint(span, ref position);
            values.Add(kind switch
            {
                ProtoSchemaFieldKind.UInt64 => JsonValue.Create(value.ToString()),
                ProtoSchemaFieldKind.Int32 => JsonValue.Create(unchecked((int)value)),
                ProtoSchemaFieldKind.Bool => JsonValue.Create(value != 0),
                _ => JsonValue.Create(checked((long)value))
            });
        }

        return values;
    }

    private static JsonNode DecodeNestedMessage(byte[] payload, string? messageType, int depth)
    {
        return !string.IsNullOrWhiteSpace(messageType) && KnownPacketSchemas.TryGetMessage(messageType, out _)
            ? DecodeMessage(payload, messageType, depth)
            : ProtobufReader.DecodeGeneric(payload, depth);
    }

    private static JsonNode DecodeUnknownField(ProtoField field, int depth)
    {
        return field.WireType switch
        {
            ProtoWireType.Varint => JsonValue.Create(checked((long)field.Varint)),
            ProtoWireType.Fixed32 => JsonValue.Create(BinaryPrimitives.ReadUInt32LittleEndian(field.Bytes)),
            ProtoWireType.Fixed64 => JsonValue.Create(BinaryPrimitives.ReadUInt64LittleEndian(field.Bytes).ToString()),
            ProtoWireType.LengthDelimited => DecodeUnknownLengthDelimited(field.Bytes, depth + 1),
            _ => JsonValue.Create(Convert.ToHexString(field.Bytes).ToLowerInvariant())
        };
    }

    private static JsonNode DecodeUnknownLengthDelimited(byte[] payload, int depth)
    {
        if (LooksLikeUtf8(payload))
        {
            return JsonValue.Create(ProtobufReader.DecodeString(payload));
        }

        try
        {
            return ProtobufReader.DecodeGeneric(payload, depth);
        }
        catch (ProtoException)
        {
            return JsonValue.Create(Convert.ToHexString(payload).ToLowerInvariant());
        }
    }

    private static bool LooksLikeUtf8(byte[] payload)
    {
        if (payload.Length == 0)
        {
            return true;
        }

        try
        {
            var text = StrictUtf8.GetString(payload);
            foreach (var character in text)
            {
                if (character != '\t' && character != '\r' && character != '\n' && char.IsControl(character))
                {
                    return false;
                }
            }

            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
