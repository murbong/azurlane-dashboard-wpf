using System.Text.Json.Nodes;

namespace AzurLaneDashboard.Protocol;

public sealed partial class GamePayloadDecoder
{
    private static JsonObject DecodeCommanderProfile(byte[] payload)
    {
        var result = new JsonObject { ["resource_list"] = new JsonArray() };
        var scalar = new Dictionary<int, string>
        {
            [1] = "id", [3] = "level", [4] = "exp", [6] = "attack_count", [7] = "win_count",
            [10] = "ship_bag_max", [11] = "equip_bag_max", [13] = "rank", [14] = "pvp_attack_count",
            [15] = "pvp_win_count", [18] = "buy_oil_count", [21] = "max_rank", [22] = "register_time",
            [23] = "ship_count", [24] = "acc_pay_lv", [30] = "commander_bag_max", [35] = "rmb",
            [43] = "marry_ship", [48] = "mail_storeroom_lv", [50] = "battle_ui"
        };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.WireType == ProtoWireType.Varint && scalar.TryGetValue(field.Number, out var name))
            {
                result[name] = (long)field.Varint;
            }
            else if (field.Number == 2 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result["name"] = ProtobufReader.DecodeString(field.Bytes);
            }
            else if (field.Number == 5 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("resource_list").Add(DecodeResourceInfo(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeResourceUpdate(byte[] payload)
    {
        var result = new JsonObject { ["resource_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("resource_list").Add(DecodeResourceInfo(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeShipList(byte[] payload, string propertyName, int fieldNumber)
    {
        var result = new JsonObject { [propertyName] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == fieldNumber && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array(propertyName).Add(DecodeShipInfo(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeShipListWithResult(byte[] payload)
    {
        var result = new JsonObject { ["ship_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.Varint)
            {
                result["result"] = (long)field.Varint;
            }
            else if (field.Number == 2 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("ship_list").Add(DecodeShipInfo(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeShipInfo(byte[] raw)
    {
        var result = new JsonObject();
        var scalar = new Dictionary<int, string>
        {
            [1] = "id", [2] = "template_id", [3] = "level", [4] = "exp", [6] = "energy",
            [8] = "is_locked", [11] = "intimacy", [12] = "proficiency", [14] = "create_time",
            [15] = "skin_id", [16] = "propose", [18] = "change_name_timestamp", [19] = "max_level",
            [20] = "common_flag", [21] = "activity_npc"
        };
        foreach (var field in ProtobufReader.ReadFields(raw))
        {
            if (field.WireType == ProtoWireType.Varint && scalar.TryGetValue(field.Number, out var name))
            {
                result[name] = (long)field.Varint;
            }
            else if (field.WireType == ProtoWireType.LengthDelimited)
            {
                switch (field.Number)
                {
                    case 5:
                        result.AddArrayItem("equip_info_list", DecodeEquipSkinInfo(field.Bytes));
                        break;
                    case 7:
                        result["state"] = DecodeShipState(field.Bytes);
                        break;
                    case 9:
                        result.AddArrayItem("transform_list",
                            Fields(field.Bytes, new Dictionary<int, string> { [1] = "id", [2] = "level" }));
                        break;
                    case 10:
                        result.AddArrayItem("skill_id_list",
                            Fields(field.Bytes,
                                new Dictionary<int, string> { [1] = "skill_id", [2] = "skill_lv", [3] = "skill_exp" }));
                        break;
                    case 13:
                        result.AddArrayItem("strength_list",
                            Fields(field.Bytes, new Dictionary<int, string> { [1] = "id", [2] = "exp" }));
                        break;
                    case 17:
                        result["custom_name"] = ProtobufReader.DecodeString(field.Bytes);
                        break;
                    // common_pb.lua shipinfo: 22 meta_repair_list, 23 spweapon (the equipped one), 24 skin_shadow_list.
                    case 22:
                        foreach (var value in ProtobufReader.DecodePackedUInt32(field.Bytes))
                        {
                            result.AddArrayItem("meta_repair_list", value);
                        }

                        break;
                    case 23:
                        result["spweapon"] = DecodeSpWeaponInfo(field.Bytes);
                        break;
                    case 24:
                        result.AddArrayItem("skin_shadow_list",
                            Fields(field.Bytes, new Dictionary<int, string> { [1] = "key", [2] = "value" }));
                        break;
                }
            }
            else if (field.Number == 22 && field.WireType == ProtoWireType.Varint)
            {
                result.AddArrayItem("meta_repair_list", (long)field.Varint);
            }
        }

        return result;
    }

    private static JsonObject DecodeShipState(byte[] raw) => Fields(raw, new Dictionary<int, string>
    {
        [1] = "state", [2] = "state_info_1", [3] = "state_info_2", [4] = "state_info_3", [5] = "state_info_4"
    });

    private static JsonObject DecodeResourceInfo(byte[] raw) =>
        Fields(raw, new Dictionary<int, string> { [1] = "type", [2] = "num" });
}