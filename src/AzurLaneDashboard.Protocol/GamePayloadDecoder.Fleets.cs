using System.Text.Json.Nodes;

namespace AzurLaneDashboard.Protocol;

public sealed partial class GamePayloadDecoder
{
    private static JsonObject DecodeFleetSnapshot(byte[] payload)
    {
        var result = new JsonObject { ["group_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("group_list").Add(DecodeFleetGroup(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeFleetUpdate(byte[] payload)
    {
        var result = new JsonObject();
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result["group"] = DecodeFleetGroup(field.Bytes);
            }
        }

        return result;
    }

    private static JsonObject DecodeFleetCommit(byte[] payload)
    {
        var result = new JsonObject { ["ship_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.Varint)
            {
                result["id"] = (long)field.Varint;
            }
            else if (field.Number == 2)
            {
                AddPackedOrSingle(result, "ship_list", field);
            }
        }

        return result;
    }

    private static JsonObject DecodeFleetRename(byte[] payload)
    {
        var result = new JsonObject();
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.Varint)
            {
                result["id"] = (long)field.Varint;
            }
            else if (field.Number == 2 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result["name"] = ProtobufReader.DecodeString(field.Bytes);
            }
        }

        return result;
    }

    private static JsonObject DecodeFleetGroup(byte[] raw)
    {
        var result = new JsonObject
        {
            ["ship_list"] = new JsonArray(),
            ["commanders"] = new JsonArray()
        };
        foreach (var field in ProtobufReader.ReadFields(raw))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.Varint)
            {
                result["id"] = (long)field.Varint;
            }
            else if (field.Number == 2 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result["name"] = ProtobufReader.DecodeString(field.Bytes);
            }
            else if (field.Number == 3)
            {
                AddPackedOrSingle(result, "ship_list", field);
            }
            else if (field.Number == 4 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("commanders").Add(DecodeCommanderSlot(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeCommanderSlot(byte[] raw)
    {
        return Fields(raw, new Dictionary<int, string> { [1] = "pos", [2] = "id" });
    }
}
