using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

namespace AzurLaneDashboard.Protocol;

public enum ProtoWireType
{
    Varint = 0,
    Fixed64 = 1,
    LengthDelimited = 2,
    Fixed32 = 5
}

public readonly record struct ProtoField(int Number, ProtoWireType WireType, ulong Varint, byte[] Bytes);

public sealed class ProtoException(string message) : Exception(message);

public static class ProtobufReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static IReadOnlyList<ProtoField> ReadFields(byte[] raw)
    {
        var data = raw.AsSpan();
        var fields = new List<ProtoField>();
        var position = 0;
        while (position < data.Length)
        {
            var key = ReadVarint(data, ref position);
            var fieldNo = (int)(key >> 3);
            var wireType = (ProtoWireType)(key & 0x07);
            if (fieldNo <= 0)
            {
                throw new ProtoException("invalid field number");
            }

            switch (wireType)
            {
                case ProtoWireType.Varint:
                    fields.Add(new ProtoField(fieldNo, wireType, ReadVarint(data, ref position), []));
                    break;
                case ProtoWireType.Fixed64:
                    EnsureAvailable(data, position, 8, "truncated fixed64");
                    fields.Add(new ProtoField(fieldNo, wireType, 0, data.Slice(position, 8).ToArray()));
                    position += 8;
                    break;
                case ProtoWireType.LengthDelimited:
                    var length = checked((int)ReadVarint(data, ref position));
                    EnsureAvailable(data, position, length, "truncated bytes");
                    fields.Add(new ProtoField(fieldNo, wireType, 0, data.Slice(position, length).ToArray()));
                    position += length;
                    break;
                case ProtoWireType.Fixed32:
                    EnsureAvailable(data, position, 4, "truncated fixed32");
                    fields.Add(new ProtoField(fieldNo, wireType, 0, data.Slice(position, 4).ToArray()));
                    position += 4;
                    break;
                default:
                    throw new ProtoException($"unsupported wire type {(int)wireType}");
            }
        }

        return fields;
    }

    public static ulong ReadVarint(ReadOnlySpan<byte> data, ref int position)
    {
        var shift = 0;
        ulong result = 0;
        while (position < data.Length)
        {
            var value = data[position++];
            result |= (ulong)(value & 0x7F) << shift;
            if ((value & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
            if (shift > 63)
            {
                break;
            }
        }

        throw new ProtoException("invalid varint");
    }

    public static List<int> DecodePackedUInt32(byte[] raw)
    {
        var values = new List<int>();
        var span = raw.AsSpan();
        var position = 0;
        while (position < span.Length)
        {
            values.Add(checked((int)ReadVarint(span, ref position)));
        }

        return values;
    }

    /// <summary>
    /// UTF-8, as the game sends its own data. The KR gateway's server list (SC 10021) names servers in CP949, which
    /// UTF-8 would turn into replacement characters, so text that is not valid UTF-8 is read as CP949.
    /// </summary>
    public static string DecodeString(byte[] raw) =>
        System.Text.Unicode.Utf8.IsValid(raw) ? Encoding.UTF8.GetString(raw) : KoreanEncoding.Value.GetString(raw);

    private static readonly Lazy<Encoding> KoreanEncoding = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    });

    public static JsonObject DecodeUInt32Fields(byte[] raw, IReadOnlyDictionary<int, string> names)
    {
        var result = new JsonObject();
        foreach (var field in ReadFields(raw))
        {
            if (field.WireType == ProtoWireType.Varint && names.TryGetValue(field.Number, out var name))
            {
                result[name] = checked((long)field.Varint);
            }
        }

        return result;
    }

    public static JsonObject DecodeGeneric(byte[] raw, int depth = 0)
    {
        var result = new JsonObject();
        if (depth > 8)
        {
            result["raw"] = Convert.ToHexString(raw).ToLowerInvariant();
            return result;
        }

        foreach (var field in ReadFields(raw))
        {
            JsonNode value = field.WireType switch
            {
                ProtoWireType.Varint => JsonValue.Create(checked((long)field.Varint)),
                ProtoWireType.Fixed32 => JsonValue.Create(BinaryPrimitives.ReadUInt32LittleEndian(field.Bytes)),
                ProtoWireType.Fixed64 => JsonValue.Create(BinaryPrimitives.ReadUInt64LittleEndian(field.Bytes)),
                ProtoWireType.LengthDelimited => DecodeLengthDelimitedGeneric(field.Bytes, depth),
                _ => JsonValue.Create(Convert.ToHexString(field.Bytes).ToLowerInvariant())
            };
            AddRepeated(result, $"field_{field.Number}", value);
        }

        return result;
    }

    public static void AddRepeated(JsonObject target, string propertyName, JsonNode? value)
    {
        value ??= JsonValue.Create("");
        if (target[propertyName] is JsonArray existing)
        {
            existing.Add(value);
            return;
        }

        if (target.TryGetPropertyValue(propertyName, out var current) && current is not null)
        {
            target[propertyName] = null;
            target[propertyName] = new JsonArray(current, value);
            return;
        }

        target[propertyName] = value;
    }

    public static void AddPackedOrSingle(JsonObject target, string propertyName, ProtoField field)
    {
        if (field.WireType == ProtoWireType.Varint)
        {
            AddRepeated(target, propertyName, JsonValue.Create(checked((long)field.Varint)));
        }
        else if (field.WireType == ProtoWireType.LengthDelimited)
        {
            foreach (var value in DecodePackedUInt32(field.Bytes))
            {
                AddRepeated(target, propertyName, JsonValue.Create(value));
            }
        }
    }

    private static JsonNode DecodeLengthDelimitedGeneric(byte[] raw, int depth)
    {
        if (LooksLikeUtf8(raw))
        {
            return JsonValue.Create(DecodeString(raw));
        }

        try
        {
            return DecodeGeneric(raw, depth + 1);
        }
        catch (ProtoException)
        {
            return JsonValue.Create(Convert.ToHexString(raw).ToLowerInvariant());
        }
    }

    private static bool LooksLikeUtf8(byte[] raw)
    {
        if (raw.Length == 0)
        {
            return true;
        }

        try
        {
            var text = StrictUtf8.GetString(raw);
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

    private static void EnsureAvailable(ReadOnlySpan<byte> data, int position, int length, string message)
    {
        if (length < 0 || position + length > data.Length)
        {
            throw new ProtoException(message);
        }
    }
}
