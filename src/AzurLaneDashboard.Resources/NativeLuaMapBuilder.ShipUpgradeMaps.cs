using System.Globalization;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    /// <summary>
    /// What a limit break (SC 12028), META breakout (SC 63304) and strengthening (SC 12018) change, which the
    /// responses leave to the client: the next template (ship_data_breakout, ship_meta_breakout), each template's
    /// skills (buff_list) and strengthen row, and ship_data_strengthen's EXP per attribute.
    /// </summary>
    private static Dictionary<string, object?> BuildShipUpgradeMap(string luaDirectory)
    {
        Dictionary<string, int> Next(string folder, string table) =>
            LoadLuaRecords(Path.Combine(luaDirectory, folder, table + ".lua"), table, ["id", "breakout_id"])
                .Select(row => (Id: SafeInt(Get(row, "id")), Next: SafeInt(Get(row, "breakout_id"))))
                .Where(row => row.Id > 0 && row.Next > 0)
                .OrderBy(row => row.Id)
                .ToDictionary(row => row.Id.ToString(CultureInfo.InvariantCulture), row => row.Next);

        var templates = new SortedDictionary<int, Dictionary<string, object?>>();
        foreach (var row in LoadLuaRecords(
                     Path.Combine(luaDirectory, "sharecfgdata", "ship_data_template.lua"),
                     "ship_data_template",
                     ["id", "strengthen_id"],
                     ["buff_list"]))
        {
            var id = SafeInt(Get(row, "id"));
            if (id <= 0) continue;
            templates[id] = new Dictionary<string, object?>
            {
                ["strengthen_id"] = SafeInt(Get(row, "strengthen_id")),
                ["buff_list"] = IntArray(Get(row, "buff_list"))
            };
        }

        var strengthen = new SortedDictionary<int, Dictionary<string, object?>>();
        foreach (var row in LoadLuaRecords(
                     Path.Combine(luaDirectory, "sharecfg", "ship_data_strengthen.lua"),
                     "ship_data_strengthen",
                     ["id"],
                     ["durability", "level_exp", "attr_exp"]))
        {
            var id = SafeInt(Get(row, "id"));
            if (id <= 0) continue;
            strengthen[id] = new Dictionary<string, object?>
            {
                ["durability"] = IntArray(Get(row, "durability")),
                ["level_exp"] = IntArray(Get(row, "level_exp")),
                ["attr_exp"] = IntArray(Get(row, "attr_exp"))
            };
        }

        return new Dictionary<string, object?>
        {
            ["breakouts"] = Next("sharecfgdata", "ship_data_breakout"),
            ["meta_breakouts"] = Next("sharecfg", "ship_meta_breakout"),
            ["templates"] = templates.ToDictionary(pair => pair.Key.ToString(CultureInfo.InvariantCulture), pair => pair.Value),
            ["strengthen"] = strengthen.ToDictionary(pair => pair.Key.ToString(CultureInfo.InvariantCulture), pair => pair.Value)
        };
    }
}
