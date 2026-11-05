using System.Text.Json.Nodes;

namespace AzurLaneDashboard.Protocol;

public sealed partial class GamePayloadDecoder
{
    private static JsonObject DecodeAcademy(byte[] payload)
    {
        var result = new JsonObject { ["skill_class_list"] = new JsonArray() };
        var scalar = new Dictionary<int, string>
        {
            [1] = "oil_well_level", [2] = "oil_well_lv_up_time", [3] = "gold_well_level", [4] = "gold_well_lv_up_time",
            [5] = "class_lv", [6] = "class_lv_up_time", [9] = "skill_class_num", [10] = "daily_finish_buff_cnt"
        };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.WireType == ProtoWireType.Varint && scalar.TryGetValue(field.Number, out var name))
            {
                result[name] = (long)field.Varint;
            }
            else if (field.Number == 7 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result["class"] = Fields(field.Bytes, new Dictionary<int, string> { [1] = "proficiency" });
            }
            else if (field.Number == 8 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("skill_class_list").Add(DecodeSkillClass(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeAcademyRoomResult(byte[] payload)
    {
        var result = new JsonObject();
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.Varint)
            {
                result["result"] = (long)field.Varint;
            }
            else if (field.Number == 2 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result["class_info"] = DecodeSkillClass(field.Bytes);
            }
        }

        return result;
    }

    private static JsonObject DecodeCommissions(byte[] payload)
    {
        var result = new JsonObject { ["collection_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("collection_list").Add(DecodeCollectionInfo(field.Bytes));
            }
            else if (field.Number == 2 && field.WireType == ProtoWireType.Varint)
            {
                result["max_team"] = (long)field.Varint;
            }
        }

        return result;
    }

    private static JsonObject DecodeCommissionStart(byte[] payload)
    {
        var result = new JsonObject { ["ship_id_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.Varint)
            {
                result["id"] = (long)field.Varint;
            }
            else if (field.Number == 2)
            {
                AddPackedOrSingle(result, "ship_id_list", field);
            }
        }

        return result;
    }

    private static JsonObject DecodeCommissionUpdate(byte[] payload)
    {
        var result = new JsonObject { ["collection_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.Varint)
            {
                result["result"] = (long)field.Varint;
            }
            else if (field.Number == 2 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("collection_list").Add(DecodeCollectionInfo(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeCommissionRemove(byte[] payload)
    {
        var result = new JsonObject { ["collection"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("collection").Add(DecodeCollectionInfo(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeCommissionFinish(byte[] payload)
    {
        var result = new JsonObject { ["drop_list"] = new JsonArray(), ["new_collection"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.WireType == ProtoWireType.Varint)
            {
                if (field.Number == 1) result["result"] = (long)field.Varint;
                else if (field.Number == 2) result["exp"] = (long)field.Varint;
            }
            else if (field.WireType == ProtoWireType.LengthDelimited)
            {
                if (field.Number == 3) result.Array("drop_list").Add(DecodeDropInfo(field.Bytes));
                else if (field.Number == 4) result.Array("new_collection").Add(DecodeCollectionInfo(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeSkillClass(byte[] raw) => Fields(raw, new Dictionary<int, string>
    {
        [1] = "room_id", [2] = "ship_id", [3] = "start_time", [4] = "finish_time", [5] = "skill_pos", [6] = "exp"
    });

    private static JsonObject DecodeCollectionInfo(byte[] raw)
    {
        var result = new JsonObject { ["ship_id_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(raw))
        {
            if (field.WireType == ProtoWireType.Varint)
            {
                if (field.Number == 1) result["id"] = (long)field.Varint;
                else if (field.Number == 2) result["finish_time"] = (long)field.Varint;
                else if (field.Number == 3) result["over_time"] = (long)field.Varint;
                else if (field.Number == 4) result.Array("ship_id_list").Add((long)field.Varint);
            }
            else if (field.Number == 4 && field.WireType == ProtoWireType.LengthDelimited)
            {
                AddPackedValues(result, "ship_id_list", field.Bytes);
            }
        }

        return result;
    }
}