using System.Text.Json.Nodes;

namespace AzurLaneDashboard.Protocol;

public sealed partial class GamePayloadDecoder
{
    private static JsonObject DecodeFleetTechSnapshot(byte[] payload)
    {
        var result = new JsonObject
        {
            ["tech_list"] = new JsonArray(),
            ["techset_list"] = new JsonArray()
        };

        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("tech_list").Add(DecodeFleetTech(field.Bytes));
            }
            else if (field.Number == 2 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("techset_list").Add(DecodeFleetTechSet(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeFleetTechSetRequest(byte[] payload)
    {
        var result = new JsonObject { ["techset_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("techset_list").Add(DecodeFleetTechSet(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeFleetTechAwardResult(byte[] payload)
    {
        var result = new JsonObject { ["rewards"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.Varint)
            {
                result["result"] = (long)field.Varint;
            }
            else if (field.Number == 2 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("rewards").Add(DecodeDropInfo(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeFleetTech(byte[] raw) => Fields(raw, new Dictionary<int, string>
    {
        [1] = "group_id",
        [2] = "effect_tech_id",
        [3] = "study_tech_id",
        [4] = "study_finish_time",
        [5] = "rewarded_tech"
    });

    private static JsonObject DecodeFleetTechSet(byte[] raw) => Fields(raw, new Dictionary<int, string>
    {
        [1] = "ship_type",
        [2] = "attr_type",
        [3] = "set_value"
    });
}
