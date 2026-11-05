using System.Text.Json.Nodes;

namespace AzurLaneDashboard.Protocol;

public sealed partial class GamePayloadDecoder
{
    private static JsonObject DecodeShipMaterialRequest(byte[] payload)
    {
        var result = new JsonObject { ["material_id_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.Varint)
            {
                result["ship_id"] = (long)field.Varint;
            }
            else if (field.Number == 2)
            {
                AddPackedOrSingle(result, "material_id_list", field);
            }
        }

        return result;
    }

    private static JsonObject DecodeShipLockRequest(byte[] payload)
    {
        var result = new JsonObject { ["ship_id_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1)
            {
                AddPackedOrSingle(result, "ship_id_list", field);
            }
            else if (field.Number == 2 && field.WireType == ProtoWireType.Varint)
            {
                result["is_locked"] = (long)field.Varint;
            }
        }

        return result;
    }

    private static JsonObject DecodeShipRenameRequest(byte[] payload)
    {
        var result = new JsonObject();
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.Varint)
            {
                result["ship_id"] = (long)field.Varint;
            }
            else if (field.Number == 2 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result["name"] = ProtobufReader.DecodeString(field.Bytes);
            }
        }

        return result;
    }

    private static JsonObject DecodeShipSkinRequest(byte[] payload)
    {
        var result = new JsonObject();
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.Varint)
            {
                result["ship_id"] = (long)field.Varint;
            }
            else if (field.Number == 2 && field.WireType == ProtoWireType.Varint)
            {
                result["skin_id"] = (long)field.Varint;
            }
            else if (field.Number == 3 && field.WireType == ProtoWireType.Varint)
            {
                result["skin_shadow"] = (long)field.Varint;
            }
        }

        return result;
    }

    private static JsonObject DecodeEquipmentDestroyRequest(byte[] payload)
    {
        var result = new JsonObject { ["equip_list"] = new JsonArray() };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.LengthDelimited)
            {
                result.Array("equip_list")
                    .Add(Fields(field.Bytes, new Dictionary<int, string> { [1] = "id", [2] = "count" }));
            }
        }

        return result;
    }

    private static JsonObject DecodeSpWeaponUpgradeRequest(byte[] payload)
    {
        var result = new JsonObject
        {
            ["item_id_list"] = new JsonArray(),
            ["spweapon_id_list"] = new JsonArray()
        };
        foreach (var field in ProtobufReader.ReadFields(payload))
        {
            if (field.Number == 1 && field.WireType == ProtoWireType.Varint)
            {
                result["ship_id"] = (long)field.Varint;
            }
            else if (field.Number == 2 && field.WireType == ProtoWireType.Varint)
            {
                result["spweapon_id"] = (long)field.Varint;
            }
            else if (field.Number == 3)
            {
                AddPackedOrSingle(result, "item_id_list", field);
            }
            else if (field.Number == 4)
            {
                AddPackedOrSingle(result, "spweapon_id_list", field);
            }
        }

        return result;
    }

    private static JsonObject DecodeSpWeaponConfirmReforgeRequest(byte[] payload)
    {
        return Fields(payload, new Dictionary<int, string> { [1] = "ship_id", [2] = "spweapon_id", [3] = "cmd" });
    }
}
