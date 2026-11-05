using System.Globalization;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    /// <summary>
    /// What the client charges in oil, so the dashboard can follow oil between the server's rare resource syncs:
    /// a chapter's entry cost (chapter_template.oil, the loop table when re-running), each battle's
    /// floor(oil_at_end * fight_oil_ratio / 10000) per fleet ship capped at use_oil_limit minus the fleet's
    /// oil_at_start, as Chapter:getFleetCost does.
    /// </summary>
    private static Dictionary<string, object?> BuildOilCostMap(LuaSource data, string luaDirectory)
    {
        var ships = new SortedDictionary<int, int[]>();
        foreach (var row in data.ShipTemplates)
        {
            var id = SafeInt(Get(row, "id"));
            var start = SafeInt(Get(row, "oil_at_start"));
            var end = SafeInt(Get(row, "oil_at_end"));
            if (id > 0 && (start > 0 || end > 0))
            {
                ships[id] = [start, end];
            }
        }

        var levels = new SortedDictionary<int, int>();
        foreach (var row in data.ShipLevels)
        {
            var level = FirstPositive(SafeInt(Get(row, "level")), SafeInt(Get(row, "id")));
            var ratio = SafeInt(Get(row, "fight_oil_ratio"));
            if (level > 0 && ratio > 0)
            {
                levels[level] = ratio;
            }
        }

        var loops = LoadLuaRecords(
                Path.Combine(luaDirectory, "sharecfgdata", "chapter_template_loop.lua"),
                "chapter_template_loop",
                ["id", "oil"],
                ["use_oil_limit"])
            .ToDictionary(row => SafeInt(Get(row, "id")));
        var chapters = new SortedDictionary<int, Dictionary<string, object?>>();
        foreach (var row in data.Chapters)
        {
            var id = SafeInt(Get(row, "id"));
            var oil = SafeInt(Get(row, "oil"));
            var limit = IntArray(Get(row, "use_oil_limit"));
            if (id <= 0 || oil <= 0 && limit.Length == 0)
            {
                continue;
            }

            var entry = new Dictionary<string, object?> { ["oil"] = oil, ["limit"] = limit };
            // The loop table only overrides what it sets; Chapter:getConfig falls back to the base table.
            if (loops.TryGetValue(id, out var loop))
            {
                if (SafeInt(Get(loop, "oil")) is > 0 and var loopOil) entry["loop_oil"] = loopOil;
                if (IntArray(Get(loop, "use_oil_limit")) is { Length: > 0 } loopLimit) entry["loop_limit"] = loopLimit;
            }

            chapters[id] = entry;
        }

        return new Dictionary<string, object?>
        {
            ["ships"] = ships.ToDictionary(pair => pair.Key.ToString(CultureInfo.InvariantCulture), pair => pair.Value),
            ["levels"] = levels.ToDictionary(pair => pair.Key.ToString(CultureInfo.InvariantCulture), pair => pair.Value),
            ["chapters"] = chapters.ToDictionary(pair => pair.Key.ToString(CultureInfo.InvariantCulture), pair => pair.Value)
        };
    }
}
