using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

/// <summary>
/// The equipment bag (current_equipment_items, one row per template) and equipment skins as EquipmentProxy keeps
/// them between logins: equipping, upgrading, transforming, composing, reverting and drops move counts here.
/// </summary>
public sealed partial class CurrentStateWriter
{
    private const int EquipmentDropType = 3;
    private const int EquipmentSkinDropType = 9;

    // Responses whose rewards can hold equipment or equipment skins (drop.lua AddItemCase types 3 and 9).
    private static readonly HashSet<int> EquipmentDropPackets =
        [11203, 13006, 15003, 20006, 20012, 40004, 40008, 63004, 63016, 64006, 64008];

    private void AddBagEquipment(SqliteTransaction tx, int templateId, int delta)
    {
        if (templateId <= 0 || delta == 0) return;
        Execute(
            tx,
            """
            INSERT INTO current_equipment_items (equipment_uid, equipment_template_id, enhance_level, count, raw_json)
            VALUES ($id, $id, 0, MAX(0, $delta), json_object('id', $id, 'count', MAX(0, $delta)))
            ON CONFLICT(equipment_uid) DO UPDATE SET count = MAX(0, current_equipment_items.count + $delta)
            """,
            ("$id", templateId),
            ("$delta", delta));
        Execute(tx, "DELETE FROM current_equipment_items WHERE equipment_uid = $id AND count <= 0", ("$id", templateId));
    }

    private void AddEquipmentSkin(SqliteTransaction tx, int skinId, int delta)
    {
        if (skinId <= 0 || delta == 0) return;
        Execute(
            tx,
            """
            INSERT INTO current_equipment_skins (skin_id, count, raw_json)
            VALUES ($id, MAX(0, $delta), json_object('id', $id, 'count', MAX(0, $delta)))
            ON CONFLICT(skin_id) DO UPDATE SET count = MAX(0, current_equipment_skins.count + $delta)
            """,
            ("$id", skinId),
            ("$delta", delta));
    }

    private (int Template, int Skin) SlotEquipment(SqliteTransaction tx, string shipUid, int slot)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
                              SELECT COALESCE(equipment_template_id, 0), COALESCE(skin_id, 0)
                              FROM current_ship_equipment_slots WHERE ship_uid = $ship AND slot = $slot
                              """;
        command.Parameters.AddWithValue("$ship", shipUid);
        command.Parameters.AddWithValue("$slot", slot);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt32(0), reader.GetInt32(1)) : (0, 0);
    }

    /// <summary>EquipToShipCommand / UnequipFromShipCommand: the slot's old equipment back to the bag, the new one out.</summary>
    private void ApplySlotEquipmentChange(SqliteTransaction tx, JsonElement request)
    {
        var shipUid = Text(request, "ship_id");
        var slot = Int(request, "pos");
        var equipment = Int(request, "equip_id");
        AddBagEquipment(tx, SlotEquipment(tx, shipUid, slot).Template, 1);
        AddBagEquipment(tx, equipment, -1);
        UpsertShipEquipment(tx, shipUid, slot, equipment);
    }

    /// <summary>UpdateShipEquipmentSkinCommand: the old skin back to the skin list, the new one used.</summary>
    private void ApplySlotSkinChange(SqliteTransaction tx, JsonElement request)
    {
        var shipUid = Text(request, "ship_id");
        var slot = Int(request, "pos");
        var skin = Int(request, "equip_skin_id");
        AddEquipmentSkin(tx, SlotEquipment(tx, shipUid, slot).Skin, 1);
        AddEquipmentSkin(tx, skin, -1);
        Execute(tx, "UPDATE current_ship_equipment_slots SET skin_id = $skin WHERE ship_uid = $ship AND slot = $slot",
            ("$skin", skin), ("$ship", shipUid), ("$slot", slot));
    }

    /// <summary>
    /// UpGradeEquipmentCommands: <c>lv</c> is how many levels the equipment gains, and every level is the next
    /// template (equipment.json next = id + 1), so the slot or bag gets the template lv further on.
    /// </summary>
    private void ApplyEquipmentEnhance(SqliteTransaction tx, JsonElement request, bool onShip)
    {
        var levels = Math.Max(1, Int(request, "lv"));
        if (onShip)
        {
            var shipUid = Text(request, "ship_id");
            var slot = Int(request, "pos");
            var current = SlotEquipment(tx, shipUid, slot).Template;
            if (current <= 0) return;
            Execute(
                tx,
                """
                UPDATE current_ship_equipment_slots SET equipment_template_id = $equip, enhance_level = 0
                WHERE ship_uid = $ship AND slot = $slot
                """,
                ("$equip", current + levels), ("$ship", shipUid), ("$slot", slot));
            return;
        }

        var equipment = Int(request, "equip_id");
        AddBagEquipment(tx, equipment, -1);
        AddBagEquipment(tx, equipment + levels, 1);
    }

    /// <summary>TransformEquipmentCommand: the formula's target replaces the equipment (the formula id may differ).</summary>
    private void ApplyEquipmentTransform(SqliteTransaction tx, JsonElement request, bool onShip)
    {
        var formula = Int(request, "upgrade_id");
        var target = Int(Child(EquipmentFormulas(), "upgrades"), formula.ToString(CultureInfo.InvariantCulture));
        if (target <= 0) target = formula;
        if (onShip)
        {
            Execute(
                tx,
                """
                UPDATE current_ship_equipment_slots SET equipment_template_id = $equip, enhance_level = 0
                WHERE ship_uid = $ship AND slot = $slot
                """,
                ("$equip", target), ("$ship", Text(request, "ship_id")), ("$slot", Int(request, "pos")));
            return;
        }

        AddBagEquipment(tx, Int(request, "equip_id"), -1);
        AddBagEquipment(tx, target, 1);
    }

    /// <summary>CompositeEquipmentCommand: num of the recipe's equipment.</summary>
    private void ApplyEquipmentCompose(SqliteTransaction tx, JsonElement request)
    {
        var recipe = Int(request, "id").ToString(CultureInfo.InvariantCulture);
        AddBagEquipment(tx, Int(Child(EquipmentFormulas(), "composes"), recipe), Math.Max(1, Int(request, "num")));
    }

    /// <summary>RevertEquipmentCommand: the equipment becomes its root (+0, equipment.json base).</summary>
    private void ApplyEquipmentRevert(SqliteTransaction tx, JsonElement request)
    {
        var equipment = Int(request, "equip_id");
        var info = Child(MapFileStore.Read(ProjectLocator.FindMapsDirectory(), "equipment.json"),
            equipment.ToString(CultureInfo.InvariantCulture));
        var root = Int(info, "base");
        if (root <= 0) return;
        AddBagEquipment(tx, equipment, -1);
        AddBagEquipment(tx, root, 1);
    }

    /// <summary>Equipment and equipment skins among a response's rewards, wherever the packet lists them.</summary>
    private void ApplyEquipmentDrops(SqliteTransaction tx, JsonElement node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                var type = Int(node, "type");
                if (type is EquipmentDropType or EquipmentSkinDropType && Int(node, "id") is > 0 and var id)
                {
                    var count = Math.Max(1, FirstPositive(Int(node, "number"), Int(node, "count")));
                    if (type == EquipmentDropType) AddBagEquipment(tx, id, count);
                    else AddEquipmentSkin(tx, id, count);
                    return;
                }

                foreach (var property in node.EnumerateObject()) ApplyEquipmentDrops(tx, property.Value);
                return;
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray()) ApplyEquipmentDrops(tx, item);
                return;
        }
    }

    private static JsonElement EquipmentFormulas() =>
        MapFileStore.Read(ProjectLocator.FindMapsDirectory(), "equipment_formulas.json");

    private static JsonElement Child(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
}
