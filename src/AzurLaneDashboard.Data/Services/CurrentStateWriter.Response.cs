using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class CurrentStateWriter
{
    private StateApplyResult ApplyResponsePacket(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json,
        JsonElement request, string area)
    {
        if (Int(json, "result") != 0)
        {
            return StateApplyResult.Deferred(area,
                $"result={Int(json, "result").ToString(CultureInfo.InvariantCulture)}", packet.DecodedRowId);
        }

        switch (packet.PacketId)
        {
            case 12018:
                var strengthenMaterials = ShipUids(request, "material_id_list");
                var materialTemplates = ShipTemplates(tx, strengthenMaterials);
                var strengthenedMaterials = ReleaseShips(tx, strengthenMaterials);
                ApplyShipStrengthen(tx, Text(request, "ship_id"), materialTemplates);
                UpsertSystemStatus(tx, "ships",
                    $"강화 완료, 재료 함선 {strengthenedMaterials.ToString(CultureInfo.InvariantCulture)}척 반영",
                    new Dictionary<string, object?>
                    {
                        ["ship_id"] = Text(request, "ship_id"),
                        ["material_id_list"] = ShipUids(request, "material_id_list")
                    });
                return StateApplyResult.Applied(area, "ship strengthening materials consumed", packet.DecodedRowId);
            case 12005:
                var retired = ReleaseShips(tx, ShipUids(request, "ship_id_list"));
                UpsertSystemStatus(tx, "ships", $"퇴역 {retired.ToString(CultureInfo.InvariantCulture)}척 반영",
                    new Dictionary<string, object?>());
                return StateApplyResult.Applied(area,
                    $"ships retired ({retired.ToString(CultureInfo.InvariantCulture)})", packet.DecodedRowId);
            case 12023:
                var lockedShips = ApplyShipLock(tx, request);
                return StateApplyResult.Applied(area,
                    $"ship lock updated ({lockedShips.ToString(CultureInfo.InvariantCulture)})",
                    packet.DecodedRowId);
            case 12028:
                var breakoutMaterials = ReleaseShips(tx, ShipUids(request, "material_id_list"));
                ApplyShipBreakout(tx, Text(request, "ship_id"), "breakouts");
                UpsertSystemStatus(tx, "ships",
                    $"돌파 완료, 재료 함선 {breakoutMaterials.ToString(CultureInfo.InvariantCulture)}척 반영",
                    new Dictionary<string, object?>
                    {
                        ["ship_id"] = Text(request, "ship_id"),
                        ["material_id_list"] = ShipUids(request, "material_id_list")
                    });
                return StateApplyResult.Applied(area, "ship breakout materials consumed", packet.DecodedRowId);
            case 63304:
                ApplyShipBreakout(tx, Text(request, "ship_id"), "meta_breakouts");
                return StateApplyResult.Applied(area, "META breakout", packet.DecodedRowId);
            case 12033:
                ApplyShipPropose(tx, request, json);
                return StateApplyResult.Applied(area, "ship propose state saved", packet.DecodedRowId);
            case 12035:
                ApplyShipRename(tx, request);
                return StateApplyResult.Applied(area, "ship renamed", packet.DecodedRowId);
            case 12039:
                ApplyShipMaxLevelIncrease(tx, request);
                return StateApplyResult.Applied(area, "ship max level increased", packet.DecodedRowId);
            case 12041:
                ApplyShipFavorite(tx, request);
                return StateApplyResult.Applied(area, "ship favorite flag saved", packet.DecodedRowId);
            case 12103:
                ApplyFleetCommit(tx, packet, request);
                return StateApplyResult.Applied(area, "fleet ship list updated", packet.DecodedRowId);
            case 12105:
                ApplyFleetRename(tx, packet, request);
                return StateApplyResult.Applied(area, "fleet renamed", packet.DecodedRowId);
            case 12203:
                ApplyShipSkin(tx, request);
                return StateApplyResult.Applied(area, "ship skin updated", packet.DecodedRowId);
            case 12007:
                ApplySlotEquipmentChange(tx, request);
                return StateApplyResult.Applied(area, "ship equipment updated", packet.DecodedRowId);
            case 12037:
                ApplySlotSkinChange(tx, request);
                return StateApplyResult.Applied(area, "equipment skin updated", packet.DecodedRowId);
            case 14003:
                ApplyEquipmentEnhance(tx, request, onShip: true);
                return StateApplyResult.Applied(area, "slot enhance updated", packet.DecodedRowId);
            case 14005:
                ApplyEquipmentEnhance(tx, request, onShip: false);
                return StateApplyResult.Applied(area, "equipment enhance updated", packet.DecodedRowId);
            case 14007:
                ApplyEquipmentCompose(tx, request);
                UpsertSystemStatus(tx, "equipment",
                    $"장비 합성 완료: id={Int(request, "id").ToString(CultureInfo.InvariantCulture)}, num={Int(request, "num").ToString(CultureInfo.InvariantCulture)}",
                    new Dictionary<string, object?>
                    {
                        ["id"] = Int(request, "id"),
                        ["num"] = Int(request, "num")
                    });
                return StateApplyResult.Applied(area, "equipment compose accepted", packet.DecodedRowId);
            case 14009:
                foreach (var item in Array(request, "equip_list"))
                {
                    Execute(tx,
                        "UPDATE current_equipment_items SET count = MAX(0, count - $count) WHERE equipment_uid = $uid",
                        ("$count", Math.Max(1, Int(item, "count"))), ("$uid", Int(item, "id")));
                    Execute(tx, "DELETE FROM current_equipment_items WHERE equipment_uid = $uid AND count <= 0",
                        ("$uid", Int(item, "id")));
                }

                return StateApplyResult.Applied(area, "equipment destroyed", packet.DecodedRowId);
            case 14011:
                ApplyEquipmentRevert(tx, request);
                UpsertSystemStatus(tx, "equipment",
                    $"장비 되돌리기 완료: equip_id={Int(request, "equip_id").ToString(CultureInfo.InvariantCulture)}",
                    new Dictionary<string, object?>
                    {
                        ["equip_id"] = Int(request, "equip_id")
                    });
                return StateApplyResult.Applied(area, "equipment revert accepted", packet.DecodedRowId);
            case 14014:
                ApplyEquipmentTransform(tx, request, onShip: true);
                return StateApplyResult.Applied(area, "slot equipment upgraded", packet.DecodedRowId);
            case 14016:
                ApplyEquipmentTransform(tx, request, onShip: false);
                return StateApplyResult.Applied(area, "equipment item upgraded", packet.DecodedRowId);
            case 14202:
                var shipUid = Text(request, "ship_id");
                var spweaponId = Int(request, "spweapon_id");
                UpsertShipSpWeaponById(tx, shipUid, spweaponId);
                return StateApplyResult.Applied(area, "spweapon equipped", packet.DecodedRowId);
            case 14204:
                var consumedSpWeapons = DeleteConsumedSpWeapons(tx, request);
                UpsertSystemStatus(tx, "spweapons",
                    $"특수장비 강화 완료, 재료 장비 {consumedSpWeapons.ToString(CultureInfo.InvariantCulture)}개 반영",
                    new Dictionary<string, object?>
                    {
                        ["ship_id"] = Text(request, "ship_id"),
                        ["spweapon_id"] = Int(request, "spweapon_id"),
                        ["item_id_list"] = Array(request, "item_id_list").Select(ValueText).ToList(),
                        ["spweapon_id_list"] = Array(request, "spweapon_id_list").Select(ValueText).ToList()
                    });
                return StateApplyResult.Applied(area, "spweapon upgrade request saved", packet.DecodedRowId);
            case 14206:
                ApplySpWeaponReforge(tx, request, json);
                return StateApplyResult.Applied(area, "spweapon reforge roll saved", packet.DecodedRowId);
            case 14208:
                ApplySpWeaponConfirmReforge(tx, request);
                return StateApplyResult.Applied(area, "spweapon reforge confirmed", packet.DecodedRowId);
            case 14210:
                if (json.TryGetProperty("spweapon", out var spweapon))
                {
                    UpsertSpWeaponInventory(tx, spweapon);
                }

                return StateApplyResult.Applied(area, "spweapon created", packet.DecodedRowId);
            case 22202:
                if (json.TryGetProperty("class_info", out var classInfo))
                {
                    UpsertAcademyRoom(tx, classInfo);
                }

                return StateApplyResult.Applied(area, "academy room started", packet.DecodedRowId);
            case 22204:
                FinishLesson(tx, Int(request, "room_id"), Int(json, "exp"));
                return StateApplyResult.Applied(area, "academy room cleared", packet.DecodedRowId);
            case 22015:
                FinishLesson(tx, FirstPositive(Int(request, "roomid"), Int(request, "room_id")), null);
                return StateApplyResult.Applied(area, "academy room finished early", packet.DecodedRowId);
            case 63002:
                InsertResearch(tx, NextResearchOrder(tx), Int(request, "tech_id"), Int(request, "refresh_id"),
                    Int(json, "time"), queued: false);
                return StateApplyResult.Applied(area, "research started", packet.DecodedRowId);
            case 63006:
                Execute(tx, $"DELETE FROM current_research_queue WHERE queue_order = {ActiveResearchRow}",
                    ("$tech", Int(request, "tech_id")));
                return StateApplyResult.Applied(area, "research stopped", packet.DecodedRowId);
            case 63016:
            {
                // FinishQueueTechnologyCommand: one queue entry leaves the front per drop.
                var finished = Items(json, "drops").Count();
                Execute(
                    tx,
                    """
                    DELETE FROM current_research_queue WHERE queue_order IN (
                        SELECT queue_order FROM current_research_queue
                        WHERE COALESCE(json_extract(raw_json, '$.queued'), 1) = 1
                        ORDER BY queue_order LIMIT $count)
                    """,
                    ("$count", finished));
                return StateApplyResult.Applied(area,
                    $"research queue finished ({finished.ToString(CultureInfo.InvariantCulture)})", packet.DecodedRowId);
            }
            case 13004:
                ApplyCommissionStart(tx, request);
                return StateApplyResult.Applied(area, "commission started", packet.DecodedRowId);
            case 13008:
                Execute(
                    tx,
                    """
                    UPDATE current_commissions
                    SET finish_time = 0, ship_id_list_json = '[]',
                        raw_json = json_remove(COALESCE(raw_json, '{}'), '$.start_time', '$.ship_id_list')
                    WHERE commission_id = $id
                    """,
                    ("$id", Int(request, "id")));
                return StateApplyResult.Applied(area, "commission given up", packet.DecodedRowId);
            case 63004:
                Execute(tx, $"DELETE FROM current_research_queue WHERE queue_order = {ActiveResearchRow}",
                    ("$tech", Int(request, "tech_id")));
                UpsertSystemStatus(tx, "research",
                    $"refresh {Array(json, "refresh_list").Count().ToString(CultureInfo.InvariantCulture)}묶음",
                    new Dictionary<string, object?>
                    {
                        ["refresh_list"] = Array(json, "refresh_list").Select(JsonDictionary).ToList()
                    });
                return StateApplyResult.Applied(area, "research finished", packet.DecodedRowId);
            case 63014:
                // JoinQueueTechnologyCommand: the project being researched moves to the back of the queue.
                Execute(
                    tx,
                    $"""
                     UPDATE current_research_queue
                     SET queue_order = (SELECT MAX(queue_order) + 1 FROM current_research_queue),
                         raw_json = json_set(COALESCE(raw_json, json_object()), '$.queued', json('true'))
                     WHERE queue_order = {ActiveResearchRow}
                     """,
                    ("$tech", Int(request, "tech_id")));
                UpsertSystemStatus(tx, "research",
                    $"refresh {Array(json, "refresh_list").Count().ToString(CultureInfo.InvariantCulture)}묶음",
                    new Dictionary<string, object?>
                    {
                        ["refresh_list"] = Array(json, "refresh_list").Select(JsonDictionary).ToList()
                    });
                return StateApplyResult.Applied(area, "research refreshed", packet.DecodedRowId);
            case 64002:
                ApplyFleetTechStart(tx, packet, request);
                return StateApplyResult.Applied(area, "fleet tech study started", packet.DecodedRowId);
            case 64004:
                ApplyFleetTechFinish(tx, packet, request);
                return StateApplyResult.Applied(area, "fleet tech study finished", packet.DecodedRowId);
            case 64006:
                ApplyFleetTechAward(tx, packet, request);
                return StateApplyResult.Applied(area, "fleet tech award claimed", packet.DecodedRowId);
            case 64008:
                ApplyFleetTechOneStepAward(tx, packet);
                return StateApplyResult.Applied(area, "fleet tech awards claimed", packet.DecodedRowId);
            case 64010:
                ReplaceFleetTechAttrSettings(tx, Array(request, "techset_list"), packet.DecodedRowId);
                UpsertFleetTechStatus(tx);
                return StateApplyResult.Applied(area, "fleet tech attr settings updated", packet.DecodedRowId);
            case 13006:
                Execute(tx, "DELETE FROM current_commissions WHERE commission_id = $id", ("$id", Int(request, "id")));
                foreach (var item in Array(json, "new_collection"))
                {
                    UpsertCommission(tx, item);
                }

                return StateApplyResult.Applied(area, "commission finished", packet.DecodedRowId);
            case 12003:
                ApplyBuilds(tx, packet, json, replaceExisting: false);
                return StateApplyResult.Applied(area, "build queue started", packet.DecodedRowId);
            case 12009:
                DeleteBuildPositions(tx, Array(json, "pos_list").Concat(Array(request, "pos_list")).Select(ValueText));
                return StateApplyResult.Applied(area, "build position cleared", packet.DecodedRowId);
            case 12044:
                DeleteBuildPositions(tx, Array(json, "info_list").Select(item => Text(item, "pos")));
                return StateApplyResult.Applied(area, "build quick finish cleared", packet.DecodedRowId);
            case 12046:
                UpsertSystemStatus(tx, "builds", "건조 취소 반영", new Dictionary<string, object?>
                {
                    ["type"] = Int(request, "type")
                });
                return StateApplyResult.Applied(area, "build cancel accepted", packet.DecodedRowId);
            case 40004:
                ApplyBattleResult(tx, packet, json, request);
                return StateApplyResult.Applied(area, "battle result", packet.DecodedRowId);
            case 25005 or 25007 or 25009 or 25013 or 25015 or 25017 or 25021:
                return ApplyCommanderResponse(tx, packet, json, request, area);
            default:
                return StateApplyResult.Deferred(area, "response handler not implemented", packet.DecodedRowId);
        }
    }

    private void UpsertShipEquipment(SqliteTransaction tx, string shipUid, int slot, int equipmentId)
    {
        if (string.IsNullOrWhiteSpace(shipUid) || slot <= 0)
        {
            return;
        }

        Execute(
            tx,
            """
            INSERT INTO current_ship_equipment_slots (
                ship_uid, slot, empty, equipment_template_id, enhance_level, skin_id, raw_json)
            VALUES ($ship, $slot, $empty, $equip, 0, 0, $raw)
            ON CONFLICT(ship_uid, slot) DO UPDATE SET
                empty = excluded.empty,
                equipment_template_id = excluded.equipment_template_id,
                raw_json = excluded.raw_json
            """,
            ("$ship", shipUid),
            ("$slot", slot),
            ("$empty", equipmentId <= 0 ? 1 : 0),
            ("$equip", equipmentId),
            ("$raw",
                Json(new Dictionary<string, object?> { ["slot"] = slot, ["equipment_template_id"] = equipmentId })));
    }

    private void UpsertShipSpWeaponById(SqliteTransaction tx, string shipUid, int spweaponId)
    {
        if (string.IsNullOrWhiteSpace(shipUid))
        {
            return;
        }

        if (spweaponId <= 0)
        {
            Execute(tx, "DELETE FROM current_ship_special_equipment WHERE ship_uid = $ship", ("$ship", shipUid));
            return;
        }

        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
                              SELECT template_id, enhance_level, attr_1, attr_2, effect, pt, raw_json
                              FROM current_spweapon_inventory
                              WHERE spweapon_uid = $uid
                              LIMIT 1
                              """;
        command.Parameters.AddWithValue("$uid", spweaponId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            Execute(
                tx,
                """
                INSERT INTO current_ship_special_equipment
                    (ship_uid, spweapon_uid, template_id, enhance_level, attr_1, attr_2, effect, pt, raw_json)
                VALUES ($ship, $uid, 0, 0, 0, 0, 0, 0, $raw)
                ON CONFLICT(ship_uid) DO UPDATE SET
                    spweapon_uid = excluded.spweapon_uid,
                    raw_json = excluded.raw_json
                """,
                ("$ship", shipUid),
                ("$uid", spweaponId),
                ("$raw", Json(new Dictionary<string, object?> { ["id"] = spweaponId })));
            return;
        }

        var values = new object?[6];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = reader.IsDBNull(i) ? "" : reader.GetValue(i);
        }

        var raw = reader.IsDBNull(6)
            ? Json(new Dictionary<string, object?> { ["id"] = spweaponId })
            : reader.GetString(6);
        reader.Close();
        Execute(
            tx,
            """
            INSERT INTO current_ship_special_equipment
                (ship_uid, spweapon_uid, template_id, enhance_level, attr_1, attr_2, effect, pt, raw_json)
            VALUES ($ship, $uid, $template, $enhance, $attr1, $attr2, $effect, $pt, $raw)
            ON CONFLICT(ship_uid) DO UPDATE SET
                spweapon_uid = excluded.spweapon_uid,
                template_id = excluded.template_id,
                enhance_level = excluded.enhance_level,
                attr_1 = excluded.attr_1,
                attr_2 = excluded.attr_2,
                effect = excluded.effect,
                pt = excluded.pt,
                raw_json = excluded.raw_json
            """,
            ("$ship", shipUid),
            ("$uid", spweaponId),
            ("$template", ToInt(values[0])),
            ("$enhance", ToInt(values[1])),
            ("$attr1", ToInt(values[2])),
            ("$attr2", ToInt(values[3])),
            ("$effect", ToInt(values[4])),
            ("$pt", ToInt(values[5])),
            ("$raw", raw));
    }
}
