using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class CurrentStateWriter
{
    private StateApplyResult ApplyEquipmentSnapshot(SqliteTransaction tx, DecodedPacketEnvelope packet,
        JsonElement json)
    {
        Execute(tx, "DELETE FROM current_equipment_items");
        var count = 0;
        foreach (var item in Array(json, "equip_list"))
        {
            var itemId = Int(item, "id");
            var templateId = FirstPositive(Int(item, "template_id"), Int(item, "config_id"), Int(item, "equip_id"),
                itemId);
            var itemCount = Math.Max(1, FirstPositive(Int(item, "count"), Int(item, "num")));
            if (itemId > 1000 && templateId is > 0 and <= 20 && Int(item, "count") <= 0)
            {
                itemCount = templateId;
                templateId = itemId;
            }

            var uid = FirstPositive(Int(item, "uid"), Int(item, "equipment_uid"), itemId, templateId);
            if (uid <= 0 || templateId <= 0)
            {
                continue;
            }

            Execute(
                tx,
                """
                INSERT INTO current_equipment_items (
                    equipment_uid, equipment_template_id, enhance_level, count, raw_json)
                VALUES ($uid, $template, $enhance, $count, $raw)
                """,
                ("$uid", uid),
                ("$template", templateId),
                ("$enhance", FirstPositive(Int(item, "enhance_level"), Int(item, "lv"))),
                ("$count", itemCount),
                ("$raw", Json(item)));
            count++;
        }

        ApplySpWeaponSnapshot(tx, packet, json);
        return StateApplyResult.Applied("equipment", $"{count.ToString(CultureInfo.InvariantCulture)} equipment",
            packet.DecodedRowId);
    }

    private StateApplyResult ApplySpWeaponSnapshot(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        var count = 0;
        if (json.TryGetProperty("spweapon_list", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            // The login list (SC 14001) replaces the inventory; SC 14200 only adds (EquipmentProxy.AddSpWeapon).
            if (packet.PacketId != 14200) Execute(tx, "DELETE FROM current_spweapon_inventory");
            foreach (var item in list.EnumerateArray())
            {
                UpsertSpWeaponInventory(tx, item);
                count++;
            }
        }

        return StateApplyResult.Applied("spweapons", $"{count.ToString(CultureInfo.InvariantCulture)} spweapons",
            packet.DecodedRowId);
    }

    private void UpsertSpWeaponInventory(SqliteTransaction tx, JsonElement item)
    {
        var uid = Int(item, "id");
        if (uid <= 0)
        {
            return;
        }

        Execute(
            tx,
            """
            INSERT INTO current_spweapon_inventory (
                spweapon_uid, template_id, enhance_level, attr_1, attr_2, attr_temp_1, attr_temp_2, effect, pt, raw_json)
            VALUES ($uid, $template, $enhance, $attr1, $attr2, $temp1, $temp2, $effect, $pt, $raw)
            ON CONFLICT(spweapon_uid) DO UPDATE SET
                template_id = excluded.template_id,
                attr_1 = excluded.attr_1,
                attr_2 = excluded.attr_2,
                attr_temp_1 = excluded.attr_temp_1,
                attr_temp_2 = excluded.attr_temp_2,
                effect = excluded.effect,
                pt = excluded.pt,
                raw_json = json_patch(COALESCE(current_spweapon_inventory.raw_json, '{}'), excluded.raw_json)
            """,
            ("$uid", uid),
            ("$template", Int(item, "template_id")),
            ("$enhance", Int(item, "enhance_level")),
            ("$attr1", Int(item, "attr_1")),
            ("$attr2", Int(item, "attr_2")),
            ("$temp1", Int(item, "attr_temp_1")),
            ("$temp2", Int(item, "attr_temp_2")),
            ("$effect", Int(item, "effect")),
            ("$pt", Int(item, "pt")),
            ("$raw", Json(item)));
    }

    private void UpsertShipSpWeapon(SqliteTransaction tx, string shipUid, JsonElement item)
    {
        var uid = Int(item, "id");
        if (uid <= 0)
        {
            Execute(tx, "DELETE FROM current_ship_special_equipment WHERE ship_uid = $ship", ("$ship", shipUid));
            return;
        }

        Execute(
            tx,
            """
            INSERT INTO current_ship_special_equipment (
                ship_uid, spweapon_uid, template_id, enhance_level, attr_1, attr_2, effect, pt, raw_json)
            VALUES ($ship, $uid, $template, $enhance, $attr1, $attr2, $effect, $pt, $raw)
            ON CONFLICT(ship_uid) DO UPDATE SET
                spweapon_uid = excluded.spweapon_uid,
                template_id = excluded.template_id,
                attr_1 = excluded.attr_1,
                attr_2 = excluded.attr_2,
                effect = excluded.effect,
                pt = excluded.pt,
                raw_json = excluded.raw_json
            """,
            ("$ship", shipUid),
            ("$uid", uid),
            ("$template", Int(item, "template_id")),
            ("$enhance", Int(item, "enhance_level")),
            ("$attr1", Int(item, "attr_1")),
            ("$attr2", Int(item, "attr_2")),
            ("$effect", Int(item, "effect")),
            ("$pt", Int(item, "pt")),
            ("$raw", Json(item)));
    }

    private StateApplyResult ApplyEquipmentSkins(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        Execute(tx, "DELETE FROM current_equipment_skins");
        var count = 0;
        foreach (var item in Array(json, "equip_skin_list"))
        {
            var id = Int(item, "id");
            if (id <= 0)
            {
                continue;
            }

            Execute(
                tx,
                """
                INSERT INTO current_equipment_skins (skin_id, count, raw_json)
                VALUES ($id, $count, $raw)
                """,
                ("$id", id),
                ("$count", Math.Max(1, Int(item, "count"))),
                ("$raw", Json(item)));
            count++;
        }

        return StateApplyResult.Applied("equipment_skins", $"{count.ToString(CultureInfo.InvariantCulture)} skins",
            packet.DecodedRowId);
    }

    private StateApplyResult ApplyItems(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        Execute(tx, "DELETE FROM current_items");
        var count = 0;
        foreach (var (kind, item) in Array(json, "item_list").Select(item => ("item", item))
                     .Concat(Array(json, "limit_list").Select(item => ("limit", item)))
                     .Concat(Array(json, "item_misc_list").Select(item => ("misc", item))))
        {
            var id = Int(item, "id");
            if (id <= 0)
            {
                continue;
            }

            Execute(
                tx,
                """
                INSERT INTO current_items (item_id, count, item_type, raw_json)
                VALUES ($id, $count, $kind, $raw)
                ON CONFLICT(item_id) DO UPDATE SET
                    count = excluded.count,
                    item_type = excluded.item_type,
                    raw_json = excluded.raw_json
                """,
                ("$id", id),
                ("$count", FirstPositive(Int(item, "count"), Int(item, "data"))),
                ("$kind", kind),
                ("$raw", Json(item)));
            count++;
        }

        return StateApplyResult.Applied("items", $"{count.ToString(CultureInfo.InvariantCulture)} items",
            packet.DecodedRowId);
    }

}
