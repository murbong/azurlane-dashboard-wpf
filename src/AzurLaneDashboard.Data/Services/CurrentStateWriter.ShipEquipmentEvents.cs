using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class CurrentStateWriter
{
    // What a ship's rows live in; SC 12001 clears them all, retiring a ship clears its own.
    private static readonly string[] ShipTables =
    [
        "current_ships", "current_ship_skills", "current_ship_equipment_slots", "current_ship_special_equipment"
    ];

    private static List<string> ShipUids(JsonElement request, string property) =>
        Items(request, property).Select(ValueText).Where(uid => uid.Length > 0 && uid != "0").ToList();

    /// <summary>
    /// DestroyShipsCommand, and the materials of UpgradeStarCommand and ModShipCommand: each ship's equipment and
    /// equipment skins go back to the bag and its special equipment to the spweapon inventory before the ship is
    /// removed. Gold and items come with the next sync.
    /// </summary>
    private int ReleaseShips(SqliteTransaction tx, IEnumerable<string> shipUids)
    {
        var count = 0;
        foreach (var shipUid in shipUids)
        {
            Execute(
                tx,
                """
                INSERT INTO current_equipment_items (equipment_uid, equipment_template_id, enhance_level, count, raw_json)
                SELECT equipment_template_id, equipment_template_id, 0, COUNT(*),
                       json_object('id', equipment_template_id, 'count', COUNT(*))
                FROM current_ship_equipment_slots
                WHERE ship_uid = $ship AND equipment_template_id > 0
                GROUP BY equipment_template_id
                ON CONFLICT(equipment_uid) DO UPDATE SET count = current_equipment_items.count + excluded.count
                """,
                ("$ship", shipUid));
            Execute(
                tx,
                """
                INSERT INTO current_equipment_skins (skin_id, count, raw_json)
                SELECT skin_id, COUNT(*), json_object('id', skin_id, 'count', COUNT(*))
                FROM current_ship_equipment_slots
                WHERE ship_uid = $ship AND skin_id > 0
                GROUP BY skin_id
                ON CONFLICT(skin_id) DO UPDATE SET count = current_equipment_skins.count + excluded.count
                """,
                ("$ship", shipUid));
            Execute(
                tx,
                """
                INSERT INTO current_spweapon_inventory (
                    spweapon_uid, template_id, enhance_level, attr_1, attr_2, attr_temp_1, attr_temp_2, effect, pt, raw_json)
                SELECT spweapon_uid, template_id, enhance_level, attr_1, attr_2, 0, 0, effect, pt, raw_json
                FROM current_ship_special_equipment
                WHERE ship_uid = $ship AND spweapon_uid > 0
                ON CONFLICT(spweapon_uid) DO NOTHING
                """,
                ("$ship", shipUid));
            DeleteCurrentShip(tx, shipUid);
            count++;
        }

        return count;
    }

    private void DeleteCurrentShip(SqliteTransaction tx, string shipUid)
    {
        Execute(tx, "DELETE FROM current_ships WHERE ship_uid = $ship", ("$ship", shipUid));
        Execute(tx, "DELETE FROM current_ship_skills WHERE ship_uid = $ship", ("$ship", shipUid));
        Execute(tx, "DELETE FROM current_ship_equipment_slots WHERE ship_uid = $ship", ("$ship", shipUid));
        Execute(tx, "DELETE FROM current_ship_special_equipment WHERE ship_uid = $ship", ("$ship", shipUid));
        Execute(tx, "DELETE FROM current_fleet_ships WHERE ship_uid = $ship", ("$ship", shipUid));
        Execute(tx, "DELETE FROM current_battle_ship_rates WHERE ship_uid = $ship", ("$ship", shipUid));
    }

    private int ApplyShipLock(SqliteTransaction tx, JsonElement request)
    {
        var count = 0;
        var locked = Int(request, "is_locked") != 0 ? 1 : 0;
        var raw = Json(new Dictionary<string, object?> { ["is_locked"] = locked });
        foreach (var shipId in Array(request, "ship_id_list"))
        {
            var shipUid = ValueText(shipId);
            if (string.IsNullOrWhiteSpace(shipUid) || shipUid == "0")
            {
                continue;
            }

            Execute(
                tx,
                "UPDATE current_ships SET locked = $locked, raw_json = json_patch(COALESCE(raw_json, '{}'), $raw) WHERE ship_uid = $ship",
                ("$locked", locked),
                ("$raw", raw),
                ("$ship", shipUid));
            count++;
        }

        return count;
    }

    private void ApplyShipRename(SqliteTransaction tx, JsonElement request)
    {
        var shipUid = Text(request, "ship_id");
        var name = Text(request, "name");
        if (string.IsNullOrWhiteSpace(shipUid) || shipUid == "0")
        {
            return;
        }

        Execute(
            tx,
            """
            UPDATE current_ships
            SET custom_name = $name,
                display_name = CASE WHEN $name <> '' THEN $name ELSE ship_uid END,
                raw_json = json_patch(COALESCE(raw_json, '{}'), $raw)
            WHERE ship_uid = $ship
            """,
            ("$name", name),
            ("$raw", Json(new Dictionary<string, object?> { ["custom_name"] = name })),
            ("$ship", shipUid));
    }

    private void ApplyShipSkin(SqliteTransaction tx, JsonElement request)
    {
        var shipUid = Text(request, "ship_id");
        if (string.IsNullOrWhiteSpace(shipUid) || shipUid == "0")
        {
            return;
        }

        var skinId = Int(request, "skin_id");
        Execute(
            tx,
            """
            UPDATE current_ships
            SET skin_id = $skin,
                raw_json = json_patch(COALESCE(raw_json, '{}'), $raw)
            WHERE ship_uid = $ship
            """,
            ("$skin", skinId),
            ("$raw", Json(new Dictionary<string, object?> { ["skin_id"] = skinId })),
            ("$ship", shipUid));
    }

    private void ApplyShipPropose(SqliteTransaction tx, JsonElement request, JsonElement response)
    {
        var shipUid = Text(request, "ship_id");
        if (string.IsNullOrWhiteSpace(shipUid) || shipUid == "0")
        {
            return;
        }

        Execute(
            tx,
            """
            UPDATE current_ships
            SET raw_json = json_patch(COALESCE(raw_json, '{}'), $raw)
            WHERE ship_uid = $ship
            """,
            ("$raw", Json(new Dictionary<string, object?>
            {
                ["propose"] = 1,
                ["propose_time"] = Int(response, "time")
            })),
            ("$ship", shipUid));
    }

    private void ApplyShipMaxLevelIncrease(SqliteTransaction tx, JsonElement request)
    {
        var shipUid = Text(request, "ship_id");
        if (string.IsNullOrWhiteSpace(shipUid) || shipUid == "0")
        {
            return;
        }

        Execute(
            tx,
            """
            UPDATE current_ships
            SET max_level = CASE WHEN max_level >= 70 THEN max_level + 5 ELSE max_level END,
                raw_json = json_patch(COALESCE(raw_json, '{}'), $raw)
            WHERE ship_uid = $ship
            """,
            ("$raw", Json(new Dictionary<string, object?> { ["max_level_increment"] = 5 })),
            ("$ship", shipUid));
    }

    private void ApplyShipFavorite(SqliteTransaction tx, JsonElement request)
    {
        var shipUid = Text(request, "ship_id");
        if (string.IsNullOrWhiteSpace(shipUid) || shipUid == "0")
        {
            return;
        }

        var flag = Int(request, "flag");
        Execute(
            tx,
            """
            UPDATE current_ships
            SET raw_json = json_patch(COALESCE(raw_json, '{}'), $raw)
            WHERE ship_uid = $ship
            """,
            ("$raw", Json(new Dictionary<string, object?> { ["favorite_flag"] = flag })),
            ("$ship", shipUid));
    }

    private int DeleteConsumedSpWeapons(SqliteTransaction tx, JsonElement request)
    {
        var count = 0;
        foreach (var spweaponId in Array(request, "spweapon_id_list"))
        {
            var uid = ToInt(ValueText(spweaponId));
            if (uid <= 0)
            {
                continue;
            }

            Execute(tx, "DELETE FROM current_spweapon_inventory WHERE spweapon_uid = $uid", ("$uid", uid));
            Execute(tx, "DELETE FROM current_ship_special_equipment WHERE spweapon_uid = $uid", ("$uid", uid));
            count++;
        }

        return count;
    }

    private void ApplySpWeaponReforge(SqliteTransaction tx, JsonElement request, JsonElement response)
    {
        var uid = Int(request, "spweapon_id");
        if (uid <= 0)
        {
            return;
        }

        var temp1 = Int(response, "attr_temp_1");
        var temp2 = Int(response, "attr_temp_2");
        var raw = Json(new Dictionary<string, object?>
        {
            ["attr_temp_1"] = temp1,
            ["attr_temp_2"] = temp2
        });

        Execute(
            tx,
            """
            UPDATE current_spweapon_inventory
            SET attr_temp_1 = $temp1,
                attr_temp_2 = $temp2,
                raw_json = json_patch(COALESCE(raw_json, '{}'), $raw)
            WHERE spweapon_uid = $uid
            """,
            ("$temp1", temp1),
            ("$temp2", temp2),
            ("$raw", raw),
            ("$uid", uid));
    }

    private void ApplySpWeaponConfirmReforge(SqliteTransaction tx, JsonElement request)
    {
        var uid = Int(request, "spweapon_id");
        if (uid <= 0)
        {
            return;
        }

        var cmd = Int(request, "cmd");
        if (cmd == 1)
        {
            Execute(
                tx,
                """
                UPDATE current_ship_special_equipment
                SET attr_1 = COALESCE((SELECT attr_temp_1 FROM current_spweapon_inventory WHERE spweapon_uid = $uid), attr_1),
                    attr_2 = COALESCE((SELECT attr_temp_2 FROM current_spweapon_inventory WHERE spweapon_uid = $uid), attr_2)
                WHERE spweapon_uid = $uid
                """,
                ("$uid", uid));
        }

        Execute(
            tx,
            """
            UPDATE current_spweapon_inventory
            SET attr_1 = CASE WHEN $cmd = 1 THEN attr_temp_1 ELSE attr_1 END,
                attr_2 = CASE WHEN $cmd = 1 THEN attr_temp_2 ELSE attr_2 END,
                attr_temp_1 = 0,
                attr_temp_2 = 0,
                raw_json = json_patch(COALESCE(raw_json, '{}'), $raw)
            WHERE spweapon_uid = $uid
            """,
            ("$cmd", cmd),
            ("$raw", Json(new Dictionary<string, object?>
            {
                ["attr_temp_1"] = 0,
                ["attr_temp_2"] = 0,
                ["reforge_confirm_cmd"] = cmd
            })),
            ("$uid", uid));
    }

    private static int ToInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }
}
