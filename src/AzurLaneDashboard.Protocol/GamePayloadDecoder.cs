using System.Text.Json;
using System.Text.Json.Nodes;

namespace AzurLaneDashboard.Protocol;

public sealed partial class GamePayloadDecoder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public string DecodeToJson(GamePacket packet)
    {
        return Decode(packet.PacketId, packet.Payload).ToJsonString(JsonOptions);
    }

    public JsonObject Decode(int packetId, byte[] payload)
    {
        try
        {
            return packetId switch
            {
                11003 => DecodeCommanderProfile(payload),
                11004 => DecodeResourceUpdate(payload),
                12001 => DecodeShipList(payload, "shiplist", 1),
                12002 => Fields(payload, new Dictionary<int, string> { [1] = "id", [2] = "count", [3] = "costtype" }),
                12003 => DecodeBuildResult(payload),
                12006 => Fields(payload,
                    new Dictionary<int, string> { [1] = "ship_id", [2] = "equip_id", [3] = "pos", [4] = "type" }),
                12007 => Result(payload),
                12008 => DecodeBuildPositionRequest(payload),
                12009 => DecodeBuildPositionResult(payload),
                12010 => DecodeShipList(payload, "ship_list", 1),
                12017 => DecodeShipMaterialRequest(payload),
                12018 => Result(payload),
                12022 => DecodeShipLockRequest(payload),
                12023 => Result(payload),
                12024 => DecodeBuildQueue(payload),
                12025 => DecodeBuildPositionRequest(payload),
                12026 => DecodeShipListWithResult(payload),
                12027 => DecodeShipMaterialRequest(payload),
                12028 => Result(payload),
                12030 => DecodeShipListWithResult(payload),
                12032 => Fields(payload, new Dictionary<int, string> { [1] = "ship_id" }),
                12033 => Fields(payload, new Dictionary<int, string> { [1] = "result", [2] = "time" }),
                12034 => DecodeShipRenameRequest(payload),
                12035 => Result(payload),
                12038 => Fields(payload, new Dictionary<int, string> { [1] = "ship_id" }),
                12039 => Result(payload),
                12040 => Fields(payload, new Dictionary<int, string> { [1] = "ship_id", [2] = "flag" }),
                12041 => Result(payload),
                12101 => DecodeFleetSnapshot(payload),
                12102 => DecodeFleetCommit(payload),
                12103 => Result(payload),
                12104 => DecodeFleetRename(payload),
                12105 => Result(payload),
                12106 => DecodeFleetUpdate(payload),
                12202 => DecodeShipSkinRequest(payload),
                12203 => Result(payload),
                12036 => Fields(payload,
                    new Dictionary<int, string> { [1] = "ship_id", [2] = "equip_skin_id", [3] = "pos" }),
                12037 => Result(payload),
                12042 => DecodeShipList(payload, "ship_list", 1),
                12043 => Fields(payload, new Dictionary<int, string> { [1] = "type" }),
                12044 => DecodeBuildFinish(payload),
                12045 => Fields(payload, new Dictionary<int, string> { [1] = "type" }),
                12046 => Result(payload),
                13002 => DecodeCommissions(payload),
                13003 => DecodeCommissionStart(payload),
                13004 => Result(payload),
                13005 => Fields(payload, new Dictionary<int, string> { [1] = "id" }),
                13006 => DecodeCommissionFinish(payload),
                13007 => Fields(payload, new Dictionary<int, string> { [1] = "id" }),
                13008 => Result(payload),
                13009 => Fields(payload, new Dictionary<int, string> { [1] = "type" }),
                13010 => DecodeCommissionUpdate(payload),
                13011 => DecodeCommissionRemove(payload),
                14001 => DecodeEquipmentInventory(payload),
                14002 => Fields(payload, new Dictionary<int, string> { [1] = "ship_id", [2] = "pos", [3] = "lv" }),
                14003 => Result(payload),
                14004 => Fields(payload, new Dictionary<int, string> { [1] = "equip_id", [2] = "lv" }),
                14005 => Result(payload),
                14006 => Fields(payload, new Dictionary<int, string> { [1] = "id", [2] = "num" }),
                14007 => Result(payload),
                14008 => DecodeEquipmentDestroyRequest(payload),
                14009 => Result(payload),
                14010 => Fields(payload, new Dictionary<int, string> { [1] = "equip_id" }),
                14011 => Result(payload),
                14013 => Fields(payload,
                    new Dictionary<int, string> { [1] = "ship_id", [2] = "pos", [3] = "upgrade_id" }),
                14014 => Result(payload),
                14015 => Fields(payload, new Dictionary<int, string> { [1] = "equip_id", [2] = "upgrade_id" }),
                14016 => Result(payload),
                14101 => DecodeEquipmentSkins(payload),
                14200 => DecodeSpecialWeaponInventory(payload),
                14201 => Fields(payload, new Dictionary<int, string> { [1] = "ship_id", [2] = "spweapon_id" }),
                14202 => Result(payload),
                14203 => DecodeSpWeaponUpgradeRequest(payload),
                14204 => Result(payload),
                14205 => Fields(payload, new Dictionary<int, string> { [1] = "ship_id", [2] = "spweapon_id" }),
                14206 => Fields(payload, new Dictionary<int, string> { [1] = "result", [2] = "attr_temp_1", [3] = "attr_temp_2" }),
                14207 => DecodeSpWeaponConfirmReforgeRequest(payload),
                14208 => Result(payload),
                14210 => DecodeSpecialWeaponUpdate(payload),
                22001 => DecodeAcademy(payload),
                22009 => Fields(payload, new Dictionary<int, string> { [1] = "type" }),
                22010 => Fields(payload, new Dictionary<int, string> { [1] = "result", [2] = "exp_in_well" }),
                22013 => Fields(payload, new Dictionary<int, string> { [1] = "proficiency", [2] = "exp_in_well" }),
                22201 => Fields(payload,
                    new Dictionary<int, string>
                        { [1] = "room_id", [2] = "ship_id", [3] = "skill_pos", [4] = "item_id" }),
                22202 => DecodeAcademyRoomResult(payload),
                22203 => Fields(payload, new Dictionary<int, string> { [1] = "room_id", [2] = "type" }),
                22204 => Fields(payload, new Dictionary<int, string> { [1] = "result", [2] = "exp" }),
                40001 => DecodeBattleStart(payload),
                40003 => DecodeBattleFinishRequest(payload),
                40004 => DecodeBattleResult(payload),
                63000 => DecodeResearch(payload),
                63001 => Fields(payload, new Dictionary<int, string> { [1] = "tech_id", [2] = "refresh_id" }),
                63002 => Fields(payload, new Dictionary<int, string> { [1] = "result", [2] = "time" }),
                63003 => Fields(payload, new Dictionary<int, string> { [1] = "tech_id", [2] = "refresh_id" }),
                63004 => DecodeResearchFinish(payload),
                63005 => Fields(payload, new Dictionary<int, string> { [1] = "tech_id", [2] = "refresh_id" }),
                63007 => Fields(payload, new Dictionary<int, string> { [1] = "type" }),
                63009 => Fields(payload, new Dictionary<int, string> { [1] = "id", [2] = "target" }),
                63011 => Fields(payload, new Dictionary<int, string> { [1] = "version", [2] = "target" }),
                63013 => Fields(payload, new Dictionary<int, string> { [1] = "tech_id", [2] = "refresh_id" }),
                63015 => Fields(payload, new Dictionary<int, string> { [1] = "tech_id", [2] = "refresh_id" }),
                64000 => DecodeFleetTechSnapshot(payload),
                64001 => Fields(payload, new Dictionary<int, string> { [1] = "tech_group_id", [2] = "tech_id" }),
                64002 => Result(payload),
                64003 => Fields(payload, new Dictionary<int, string> { [1] = "tech_group_id" }),
                64004 => Result(payload),
                64005 => Fields(payload, new Dictionary<int, string> { [1] = "group_id", [2] = "tech_id" }),
                64006 => DecodeFleetTechAwardResult(payload),
                64007 => Fields(payload, new Dictionary<int, string> { [1] = "type" }),
                64008 => DecodeFleetTechAwardResult(payload),
                64009 => DecodeFleetTechSetRequest(payload),
                64010 => Result(payload),
                _ => ProtobufSchemaDecoder.Decode(packetId, payload) ?? ProtobufReader.DecodeGeneric(payload)
            };
        }
        catch (Exception ex) when (ex is ProtoException or OverflowException or ArgumentException)
        {
            return new JsonObject
            {
                ["decode_error"] = ex.Message,
                ["raw"] = Convert.ToHexString(payload).ToLowerInvariant()
            };
        }
    }
}
