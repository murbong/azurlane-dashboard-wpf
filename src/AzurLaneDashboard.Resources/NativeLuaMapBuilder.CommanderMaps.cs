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
                         "max_level", "ability_refresh_type", "painting", "exp", "group_type"]))
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
                ["painting"] = PlainText(Get(row, "painting")),
                // EXP the cat gives when eaten, before its own levels count; group_type = the kind (same-kind bonus).
                ["exp"] = SafeInt(Get(row, "exp")),
                ["group"] = SafeInt(Get(row, "group_type"))
            };
        }

        var destroyBonus = LoadLuaBlocks(Path.Combine(sharecfg, "commander_ability_template.lua"), "commander_ability_template")
            .ToDictionary(item => item.Id, item => DestroyExpBonus(item.Block));
        var talents = new SortedDictionary<int, Dictionary<string, object?>>();
        foreach (var row in LoadLuaRecords(Path.Combine(sharecfg, "commander_ability_template.lua"),
                     "commander_ability_template", ["id", "name", "group_id", "worth", "desc", "next"]))
        {
            var id = SafeInt(Get(row, "id"));
            if (id <= 0) continue;
            var (destroyValue, destroyRatio) = destroyBonus.GetValueOrDefault(id);
            talents[id] = new Dictionary<string, object?>
            {
                ["id"] = id,
                ["name"] = PlainText(Get(row, "name")),
                ["group"] = SafeInt(Get(row, "group_id")),
                ["worth"] = SafeInt(Get(row, "worth")),
                ["next"] = SafeInt(Get(row, "next")),
                ["desc"] = PlainText(Get(row, "desc")),
                ["destroy_exp"] = destroyValue,
                ["destroy_ratio"] = destroyRatio
            };
        }

        var skills = new SortedDictionary<int, Dictionary<string, object?>>();
        foreach (var row in LoadLuaRecords(Path.Combine(sharecfg, "commander_skill_template.lua"),
                     "commander_skill_template", ["id", "name", "lv", "desc", "next_id", "exp"]))
        {
            var id = SafeInt(Get(row, "id"));
            if (id <= 0) continue;
            skills[id] = new Dictionary<string, object?>
            {
                ["id"] = id,
                ["name"] = PlainText(Get(row, "name")),
                ["level"] = SafeInt(Get(row, "lv")),
                ["desc"] = PlainText(Get(row, "desc")),
                // Skill EXP to the next level (one per same-kind cat eaten); next 0 = the top level.
                ["next"] = SafeInt(Get(row, "next_id")),
                ["exp"] = SafeInt(Get(row, "exp"))
            };
        }

        // EXP each level needs, by rarity (exp_3..exp_5; exp when a rarity has no column).
        var levels = LoadLuaRecords(Path.Combine(sharecfg, "commander_level.lua"), "commander_level",
                ["level", "exp", "exp_3", "exp_4", "exp_5"])
            .Select(row => (object?)new Dictionary<string, object?>
            {
                ["level"] = SafeInt(Get(row, "level")),
                ["exp"] = SafeInt(Get(row, "exp")),
                ["exp_3"] = SafeInt(Get(row, "exp_3")),
                ["exp_4"] = SafeInt(Get(row, "exp_4")),
                ["exp_5"] = SafeInt(Get(row, "exp_5"))
            })
            .ToList();

        return new Dictionary<string, object?>
        {
            ["commanders"] = commanders.ToDictionary(pair => pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), pair => (object?)pair.Value),
            ["talents"] = talents.ToDictionary(pair => pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), pair => (object?)pair.Value),
            ["skills"] = skills.ToDictionary(pair => pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), pair => (object?)pair.Value),
            ["levels"] = levels,
            // UpgradeCommanderCommand's gameset values: the eaten cat's EXP share, the same-kind rate, skill EXP.
            ["growth"] = new Dictionary<string, object?>
            {
                ["exp_a"] = GamesetValue(luaDirectory, "commander_exp_a", 7000),
                ["same_rate"] = GamesetValue(luaDirectory, "commander_exp_same_rate", 12000),
                ["skill_exp"] = GamesetValue(luaDirectory, "commander_skill_exp", 1)
            }
        };
    }

    // CommanderTalent.getDestoryExpValue / getDestoryExpRetio: add entries {kind, {...}, {...}, attr, value} where
    // kind 1 adds a number and kind 2 a ratio (×1/10000) to attribute 202, the EXP the cat gives when eaten.
    private static (int Value, int Ratio) DestroyExpBonus(string block)
    {
        var match = Regex.Match(block, @"(?m)^\s*add\s*=\s*\{", RegexOptions.CultureInvariant);
        if (!match.Success) return (0, 0);
        var open = match.Index + match.Length - 1;
        var end = FindLuaTableEnd(block, open);
        int value = 0, ratio = 0, depth = 0;
        var scalars = new List<int>();
        foreach (Match token in Regex.Matches(block[(open + 1)..end], @"\{|\}|-?\d+", RegexOptions.CultureInvariant))
        {
            switch (token.Value)
            {
                case "{":
                    depth++;
                    if (depth == 1) scalars.Clear();
                    break;
                case "}":
                    if (depth == 1 && scalars.Count >= 3 && scalars[^2] == 202)
                    {
                        if (scalars[0] == 1) value += scalars[^1];
                        else if (scalars[0] == 2) ratio += scalars[^1];
                    }

                    depth--;
                    break;
                default:
                    if (depth == 1) scalars.Add(SafeInt(token.Value));
                    break;
            }
        }

        return (value, ratio);
    }

    private static int GamesetValue(string luaDirectory, string key, int fallback)
    {
        var path = Path.Combine(luaDirectory, "sharecfg", "gameset.lua");
        if (!File.Exists(path)) return fallback;
        var match = Regex.Match(File.ReadAllText(path),
            $@"gameset\.{Regex.Escape(key)}\s*=\s*\{{[^}}]*?key_value\s*=\s*(-?\d+)", RegexOptions.CultureInvariant);
        return match.Success ? SafeInt(match.Groups[1].Value) : fallback;
    }

    // Lua text carries Unity rich-text colour tags and stray padding.
    private static string PlainText(object? value) =>
        Regex.Replace(value?.ToString() ?? "", "<[^>]+>", "", RegexOptions.CultureInvariant).Trim();
}
