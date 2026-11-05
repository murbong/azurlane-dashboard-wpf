using System.Text.Json.Nodes;

namespace AzurLaneDashboard.Protocol;

public sealed partial class GamePayloadDecoder
{
    private static JsonObject DecodeEquipmentInventory(byte[] payload)
    {
        var result = new JsonObject
        {
            ["equip_list"] = new JsonArray(),
            ["ship_id_list"] = new JsonArray(),
            ["spweapon_list"] = new JsonArray(),
            ["spweapon_bag_size"] = 0
        };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("equip_list").Add(DecodeEquipInfo(field.Bytes));
            }
            else if (field.Number == 2)
            {
                AddPackedOrSingle(result, "ship_id_list", field);
            }
            else if (field.Number == 3 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("spweapon_list").Add(DecodeSpWeaponInfo(field.Bytes));
            }
            else if (field.Number == 4 && field.WireType == ProtoWireType.Varint)
            {
                result["spweapon_bag_size"] = (long)field.Varint;
            }
        }

        return result;
    }

    private static JsonObject DecodeEquipmentSkins(byte[] payload)
    {
        var result = new JsonObject { ["equip_skin_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("equip_skin_list")
                    .Add(Fields(field.Bytes, new Dictionary<int, string> { [1] = "id", [2] = "count" }));
            }
        }

        return result;
    }

    private static JsonObject DecodeSpecialWeaponInventory(byte[] payload)
    {
        var result = new JsonObject { ["spweapon_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("spweapon_list").Add(DecodeSpWeaponInfo(field.Bytes));
            }
        }

        return result;
    }

    private static JsonObject DecodeSpecialWeaponUpdate(byte[] payload)
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
                result["spweapon"] = DecodeSpWeaponInfo(field.Bytes);
            }
        }

        return result;
    }

    private static JsonObject DecodeEquipSkinInfo(byte[] raw) =>
        Fields(raw, new Dictionary<int, string> { [1] = "id", [2] = "skin_id" });

    private static JsonObject DecodeEquipInfo(byte[] raw) => Fields(raw, new Dictionary<int, string>
    {
        [1] = "id", [2] = "count"
    });

    private static JsonObject DecodeSpWeaponInfo(byte[] raw) => Fields(raw, new Dictionary<int, string>
    {
        [1] = "id", [2] = "template_id", [3] = "attr_1", [4] = "attr_2", [5] = "attr_temp_1",
        [6] = "attr_temp_2", [7] = "effect", [8] = "pt"
    });
}
