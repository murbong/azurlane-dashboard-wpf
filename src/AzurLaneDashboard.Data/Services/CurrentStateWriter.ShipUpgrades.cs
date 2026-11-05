using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

/// <summary>
/// Limit breaks (SC 12028), META breakouts (SC 63304) and strengthening (SC 12018) answer with a result only;
/// the client works out the new template, skills, max level and strengthen EXP from its tables
/// (maps/ship_upgrades.json, built from them), and material ships go as in a retirement.
/// </summary>
public sealed partial class CurrentStateWriter
{
    // ShipModAttr.ID_TO_ATTR: cannon, torpedo, anti-air, air, reload; ShipModLayer.IGNORE_ID leaves anti-air out.
    private static readonly int[] StrengthenAttributes = [2, 3, 5, 6];
    private const int StrengthenLevelCap = 100;

    private static JsonElement ShipUpgrades() => MapFileStore.Read(ProjectLocator.FindMapsDirectory(), "ship_upgrades.json");

    private static JsonElement ShipTemplate(int templateId) =>
        Child(MapFileStore.Read(ProjectLocator.FindMapsDirectory(), "ships.json"), templateId.ToString(CultureInfo.InvariantCulture));

    private static JsonElement UpgradeRow(string table, int id) =>
        Child(Child(ShipUpgrades(), table), id.ToString(CultureInfo.InvariantCulture));

    private static int[] IntList(JsonElement element, string property) =>
        Items(element, property).Select(item => item.ValueKind == JsonValueKind.Number ? item.GetInt32() : 0).ToArray();

    private (int Template, int Level, JsonElement Raw)? StoredShip(SqliteTransaction tx, string shipUid)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT ship_template_id, level, raw_json FROM current_ships WHERE ship_uid = $ship";
        command.Parameters.AddWithValue("$ship", shipUid);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        using var document = JsonDocument.Parse(reader.IsDBNull(2) ? "{}" : reader.GetString(2));
        return (reader.GetInt32(0), reader.GetInt32(1), document.RootElement.Clone());
    }

    /// <summary>
    /// UpgradeStarCommand / MetaCharActiveEnergyCommand.updateStar: the next template, its skills added at level 1,
    /// the old template's skills it lacks removed, and the max level raised to the new template's.
    /// </summary>
    private bool ApplyShipBreakout(SqliteTransaction tx, string shipUid, string table)
    {
        if (StoredShip(tx, shipUid) is not { } ship) return false;
        var next = Int(Child(ShipUpgrades(), table), ship.Template.ToString(CultureInfo.InvariantCulture));
        if (next <= 0) return false;

        var oldSkills = IntList(UpgradeRow("templates", ship.Template), "buff_list");
        var newSkills = IntList(UpgradeRow("templates", next), "buff_list");
        foreach (var skill in oldSkills.Except(newSkills))
        {
            Execute(tx, "DELETE FROM current_ship_skills WHERE ship_uid = $ship AND skill_id = $skill",
                ("$ship", shipUid), ("$skill", skill));
        }

        foreach (var skill in newSkills)
        {
            Execute(
                tx,
                """
                INSERT INTO current_ship_skills (ship_uid, slot_index, skill_id, skill_lv, skill_exp, raw_json)
                SELECT $ship, (SELECT COALESCE(MAX(slot_index), 0) + 1 FROM current_ship_skills WHERE ship_uid = $ship),
                       $skill, 1, 0, json_object('skill_id', $skill, 'skill_lv', 1, 'skill_exp', 0)
                WHERE NOT EXISTS (SELECT 1 FROM current_ship_skills WHERE ship_uid = $ship AND skill_id = $skill)
                """,
                ("$ship", shipUid), ("$skill", skill));
        }

        // Ship.updateMaxLevel keeps the higher of the two.
        var currentMax = Int(ship.Raw, "max_level") is > 1 and var stored ? stored : Int(ship.Raw, "commanderid");
        var maxLevel = Math.Max(currentMax, Int(ShipTemplate(next), "max_level"));
        Execute(
            tx,
            """
            UPDATE current_ships
            SET ship_template_id = $template, limit_break = $breakout, max_level = $max,
                raw_json = json_set(json_remove(COALESCE(raw_json, '{}'), '$.commanderid'),
                    '$.template_id', $template, '$.ship_template_id', $template, '$.limit_break', $breakout,
                    '$.max_level', $max)
            WHERE ship_uid = $ship
            """,
            ("$template", next), ("$breakout", LimitBreakFromTemplate(next)), ("$max", maxLevel), ("$ship", shipUid));
        return true;
    }

    /// <summary>
    /// ModShipCommand: ShipModLayer.getModExpAdditions sums each material's ship_data_strengthen attr_exp (double
    /// for the same ship group), and addModAttrExp caps the total at floor((3 + 7 * min(level, 100) / 100) *
    /// durability * 0.1) * level_exp. Event bonuses (getBuffExp) are not in the packets and are left out.
    /// </summary>
    private void ApplyShipStrengthen(SqliteTransaction tx, string shipUid, IReadOnlyList<int> materialTemplates)
    {
        if (StoredShip(tx, shipUid) is not { } ship || materialTemplates.Count == 0) return;
        var group = Int(ShipTemplate(ship.Template), "group_type");
        var limits = UpgradeRow("strengthen", Int(UpgradeRow("templates", ship.Template), "strengthen_id"));
        var durability = IntList(limits, "durability");
        var levelExp = IntList(limits, "level_exp");

        var current = new Dictionary<int, int>();
        var order = new List<int>();
        foreach (var item in Items(ship.Raw, "strength_list"))
        {
            var id = Int(item, "id");
            if (!current.ContainsKey(id)) order.Add(id);
            current[id] = Int(item, "exp");
        }

        foreach (var attribute in StrengthenAttributes)
        {
            var index = attribute - 2;
            if (index >= durability.Length) continue;
            var gained = 0;
            foreach (var material in materialTemplates)
            {
                var attrExp = IntList(UpgradeRow("strengthen", Int(UpgradeRow("templates", material), "strengthen_id")), "attr_exp");
                var value = index < attrExp.Length ? attrExp[index] : 0;
                gained += Int(ShipTemplate(material), "group_type") == group && group > 0 ? value * 2 : value;
            }

            var top = (int)Math.Floor((3 + 7 * Math.Min(ship.Level, StrengthenLevelCap) / 100.0) * durability[index] * 0.1 + 1e-9);
            if (top == 0 || gained == 0) continue;
            var ratio = Math.Max(index < levelExp.Length ? levelExp[index] : 1, 1);
            if (!current.ContainsKey(attribute)) order.Add(attribute);
            current[attribute] = Math.Min(current.GetValueOrDefault(attribute) + gained, top * ratio);
        }

        var list = new JsonArray(order.Select(id => (JsonNode)new JsonObject { ["id"] = id, ["exp"] = current[id] }).ToArray());
        Execute(tx, "UPDATE current_ships SET raw_json = json_set(COALESCE(raw_json, '{}'), '$.strength_list', json($list)) WHERE ship_uid = $ship",
            ("$list", list.ToJsonString()), ("$ship", shipUid));
    }

    /// <summary>The templates of ships about to be used up, read before they are removed.</summary>
    private List<int> ShipTemplates(SqliteTransaction tx, IEnumerable<string> shipUids)
    {
        var templates = new List<int>();
        foreach (var uid in shipUids)
        {
            if (StoredShip(tx, uid) is { } ship) templates.Add(ship.Template);
        }

        return templates;
    }
}
