using System.Text.RegularExpressions;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    /// <summary>
    /// Meowfficers (지휘냥): commander_data_template for each kind, commander_ability_template for talents
    /// (group = the talent line, worth = its talent points) and commander_skill_template for the skill levels.
    /// </summary>
    private static Dictionary<string, object?> BuildCommanderMap(string luaDirectory)
    {
        var sharecfg = Path.Combine(luaDirectory, "sharecfg");
        var commanders = new SortedDictionary<int, Dictionary<string, object?>>();
        foreach (var row in LoadLuaRecords(Path.Combine(sharecfg, "commander_data_template.lua"), "commander_data_template",
                     ["id", "name", "rarity", "nationality", "skill_id", "command_value", "tactic_value", "support_value",
                         "max_level", "ability_refresh_type", "painting"]))
        {
            var id = SafeInt(Get(row, "id"));
            if (id <= 0) continue;
            commanders[id] = new Dictionary<string, object?>
            {
                ["id"] = id,
                ["name"] = PlainText(Get(row, "name")),
                // 5 SSR, 4 SR, 3 R.
                ["rarity"] = SafeInt(Get(row, "rarity")),
                ["nationality"] = SafeInt(Get(row, "nationality")),
                ["skill_id"] = SafeInt(Get(row, "skill_id")),
                ["command"] = SafeInt(Get(row, "command_value")),
                ["tactic"] = SafeInt(Get(row, "tactic_value")),
                ["support"] = SafeInt(Get(row, "support_value")),
                ["max_level"] = SafeInt(Get(row, "max_level")),
                // 1 = talents are fixed (Tempesta cats cannot reroll).
                ["refresh_type"] = SafeInt(Get(row, "ability_refresh_type")),
                ["painting"] = PlainText(Get(row, "painting"))
            };
        }

        var talents = new SortedDictionary<int, Dictionary<string, object?>>();
        foreach (var row in LoadLuaRecords(Path.Combine(sharecfg, "commander_ability_template.lua"),
                     "commander_ability_template", ["id", "name", "group_id", "worth", "desc", "next"]))
        {
            var id = SafeInt(Get(row, "id"));
            if (id <= 0) continue;
            talents[id] = new Dictionary<string, object?>
            {
                ["id"] = id,
                ["name"] = PlainText(Get(row, "name")),
                ["group"] = SafeInt(Get(row, "group_id")),
                ["worth"] = SafeInt(Get(row, "worth")),
                ["next"] = SafeInt(Get(row, "next")),
                ["desc"] = PlainText(Get(row, "desc"))
            };
        }

        var skills = new SortedDictionary<int, Dictionary<string, object?>>();
        foreach (var row in LoadLuaRecords(Path.Combine(sharecfg, "commander_skill_template.lua"),
                     "commander_skill_template", ["id", "name", "lv", "desc"]))
        {
            var id = SafeInt(Get(row, "id"));
            if (id <= 0) continue;
            skills[id] = new Dictionary<string, object?>
            {
                ["id"] = id,
                ["name"] = PlainText(Get(row, "name")),
                ["level"] = SafeInt(Get(row, "lv")),
                ["desc"] = PlainText(Get(row, "desc"))
            };
        }

        return new Dictionary<string, object?>
        {
            ["commanders"] = commanders.ToDictionary(pair => pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), pair => (object?)pair.Value),
            ["talents"] = talents.ToDictionary(pair => pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), pair => (object?)pair.Value),
            ["skills"] = skills.ToDictionary(pair => pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), pair => (object?)pair.Value)
        };
    }

    // Lua text carries Unity rich-text colour tags and stray padding.
    private static string PlainText(object? value) =>
        Regex.Replace(value?.ToString() ?? "", "<[^>]+>", "", RegexOptions.CultureInvariant).Trim();
}
