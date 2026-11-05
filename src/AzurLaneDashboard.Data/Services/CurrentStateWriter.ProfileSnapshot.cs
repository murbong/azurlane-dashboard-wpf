using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class CurrentStateWriter
{
    private StateApplyResult ApplyCommander(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        var resourceCount = UpsertResources(tx, json);
        var profileCount = 0;
        foreach (var property in json.EnumerateObject())
        {
            if (property.NameEquals("resource_list"))
            {
                continue;
            }

            Execute(
                tx,
                """
                INSERT INTO current_commander_profile (key, value_json)
                VALUES ($key, $value)
                ON CONFLICT(key) DO UPDATE SET value_json = excluded.value_json
                """,
                ("$key", property.Name),
                ("$value", Json(property.Value)));
            profileCount++;
        }

        return StateApplyResult.Applied("commander", $"resources={resourceCount}, profile={profileCount}",
            packet.DecodedRowId);
    }

    private StateApplyResult ApplyResources(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        return StateApplyResult.Applied("resources", $"{UpsertResources(tx, json)} resources", packet.DecodedRowId);
    }

    private int UpsertResources(SqliteTransaction tx, JsonElement json)
    {
        var count = 0;
        foreach (var item in Array(json, "resource_list"))
        {
            var type = Int(item, "type");
            if (type <= 0)
            {
                continue;
            }

            Execute(
                tx,
                """
                INSERT INTO current_resources (resource_type, label, num, raw_json)
                VALUES ($type, $label, $num, $raw)
                ON CONFLICT(resource_type) DO UPDATE SET
                    num = excluded.num,
                    raw_json = excluded.raw_json
                """,
                ("$type", type),
                ("$label", $"resource:{type.ToString(CultureInfo.InvariantCulture)}"),
                ("$num", Int(item, "num")),
                ("$raw", Json(item)));
            count++;
        }

        UpsertSystemStatus(tx, "resources", $"자원 {count.ToString(CultureInfo.InvariantCulture)}종 갱신",
            new Dictionary<string, object?>());
        return count;
    }

    private StateApplyResult ApplyShips(SqliteTransaction tx, DecodedPacketEnvelope packet, JsonElement json)
    {
        var ships = Array(json, "shiplist").Concat(Array(json, "ship_list")).ToList();
        // BayProxy starts over on SC 12001 (the login list; SC 12010 continues it), so ships gone since the last
        // login (retired, used as material) must not linger. Fleets come back with SC 12101.
        if (packet.PacketId == 12001 && ships.Count > 0)
        {
            foreach (var table in ShipTables) Execute(tx, $"DELETE FROM {table}");
        }

        foreach (var ship in ships)
        {
            UpsertShip(tx, packet.DecodedRowId, ship);
        }

        return StateApplyResult.Applied("ships", $"{ships.Count.ToString(CultureInfo.InvariantCulture)} ships",
            packet.DecodedRowId);
    }

    private void UpsertShip(SqliteTransaction tx, long rowId, JsonElement ship)
    {
        var uid = Text(ship, "id");
        if (string.IsNullOrWhiteSpace(uid) || uid == "0")
        {
            return;
        }

        var customName = FirstNonEmpty(Text(ship, "custom_name"), Text(ship, "name"));
        var displayName = FirstNonEmpty(customName, uid);

        Execute(
            tx,
            """
            INSERT INTO current_ships (
                ship_uid, ship_template_id, custom_name, display_name, level, exp, limit_break,
                enhance_level, skin_id, locked, intimacy, create_time, energy, max_level,
                special_equipment_json, state_json, snapshot_rowid, raw_json)
            VALUES (
                $uid, $template, $customName, $displayName, $level, $exp, $breakout,
                $enhance, $skin, $locked, $intimacy, $createTime, $energy, $maxLevel,
                $spweapon, $state, $rowid, $raw)
            ON CONFLICT(ship_uid) DO UPDATE SET
                ship_template_id = excluded.ship_template_id,
                custom_name = excluded.custom_name,
                display_name = excluded.display_name,
                level = excluded.level,
                exp = excluded.exp,
                limit_break = excluded.limit_break,
                enhance_level = excluded.enhance_level,
                skin_id = excluded.skin_id,
                locked = excluded.locked,
                intimacy = excluded.intimacy,
                create_time = CASE WHEN excluded.create_time > 0 THEN excluded.create_time ELSE current_ships.create_time END,
                energy = excluded.energy,
                max_level = excluded.max_level,
                special_equipment_json = excluded.special_equipment_json,
                state_json = excluded.state_json,
                snapshot_rowid = excluded.snapshot_rowid,
                raw_json = json_patch(json_remove(COALESCE(current_ships.raw_json, '{}'),
                    '$.commanderid', '$.blue_print_flag', '$.meta_repair_list'), excluded.raw_json)
            """,
            ("$uid", uid),
            ("$template", Int(ship, "template_id")),
            ("$customName", customName),
            ("$displayName", displayName),
            ("$level", Int(ship, "level")),
            ("$exp", Int(ship, "exp")),
            ("$breakout", FirstPositive(Int(ship, "breakout"), LimitBreakFromTemplate(Int(ship, "template_id")))),
            ("$enhance", Int(ship, "enhance_level")),
            ("$skin", Int(ship, "skin_id")),
            ("$locked", Int(ship, "is_locked")),
            ("$intimacy", Int(ship, "intimacy")),
            ("$createTime", Int(ship, "create_time")),
            ("$energy", Int(ship, "energy")),
            ("$maxLevel", Int(ship, "max_level")),
            ("$spweapon", Json(ObjectOrEmpty(ship, "spweapon"))),
            ("$state", Json(ObjectOrEmpty(ship, "state"))),
            ("$rowid", rowId),
            ("$raw", Json(ShipRaw(uid, rowId, ship))));

        Execute(tx, "DELETE FROM current_ship_skills WHERE ship_uid = $uid", ("$uid", uid));
        var slot = 1;
        foreach (var skill in Array(ship, "skill_id_list"))
        {
            Execute(
                tx,
                """
                INSERT INTO current_ship_skills (ship_uid, slot_index, skill_id, skill_lv, skill_exp, raw_json)
                VALUES ($uid, $slot, $skill, $level, $exp, $raw)
                """,
                ("$uid", uid),
                ("$slot", slot++),
                ("$skill", Int(skill, "skill_id")),
                ("$level", Int(skill, "skill_lv")),
                ("$exp", Int(skill, "skill_exp")),
                ("$raw", Json(skill)));
        }

        Execute(tx, "DELETE FROM current_ship_equipment_slots WHERE ship_uid = $uid", ("$uid", uid));
        slot = 1;
        foreach (var item in Array(ship, "equip_info_list"))
        {
            var equipId = Int(item, "id");
            Execute(
                tx,
                """
                INSERT INTO current_ship_equipment_slots (
                    ship_uid, slot, empty, equipment_template_id, enhance_level, skin_id, raw_json)
                VALUES ($uid, $slot, $empty, $equip, 0, $skin, $raw)
                """,
                ("$uid", uid),
                ("$slot", slot),
                ("$empty", equipId <= 0 ? 1 : 0),
                ("$equip", equipId),
                ("$skin", Int(item, "skin_id")),
                ("$raw",
                    Json(new Dictionary<string, object?>
                        { ["slot"] = slot, ["equipment_template_id"] = equipId, ["skin_id"] = Int(item, "skin_id") })));
            slot++;
        }

        // A full ship info without spweapon has none equipped.
        if (ship.TryGetProperty("spweapon", out var spweapon) && spweapon.ValueKind == JsonValueKind.Object)
        {
            UpsertShipSpWeapon(tx, uid, spweapon);
        }
        else
        {
            Execute(tx, "DELETE FROM current_ship_special_equipment WHERE ship_uid = $uid", ("$uid", uid));
        }
    }

}
