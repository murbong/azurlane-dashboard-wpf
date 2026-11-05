using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    private static readonly IReadOnlyDictionary<int, string> ArmorNames = new Dictionary<int, string>
    {
        [1] = "Light",
        [2] = "Medium",
        [3] = "Heavy"
    };

    // Lua ShipType ids (model/const/shiptype.lua) as the hull codes used by the community data.
    private static readonly IReadOnlyDictionary<int, string> EnemyHulls = new Dictionary<int, string>
    {
        [1] = "DD", [2] = "CL", [3] = "CA", [4] = "BC", [5] = "BB", [6] = "CVL", [7] = "CV", [8] = "SS",
        [10] = "BBV", [12] = "AR", [13] = "BM", [17] = "SSV", [18] = "CB", [19] = "AE"
    };

    /// <summary>
    /// META Showdown bosses (world_joint_boss_template). <c>state</c> is "always" (archive),
    /// "stop" (hidden) or a list of server-time windows (the rotation); the client compares it with
    /// the server clock, so the windows are kept as they are and judged at runtime.
    /// </summary>
    private static List<Dictionary<string, object?>> LoadMetaBossRecords(string path)
    {
        var records = new List<Dictionary<string, object?>>();
        foreach (var (id, block) in LoadLuaBlocks(path, "world_joint_boss_template"))
        {
            var record = ParseLuaRecordBlock(block, ["id", "name", "meta_id", "boss_level_id", "state"]);
            record.TryAdd("id", id);
            if (Get(record, "state") is not string)
            {
                record["windows"] = ParseStateWindows(LuaFieldTable(block, "state"));
            }

            record["skills"] = ParseBossSkills(LuaFieldTable(block, "description"));
            records.Add(record);
        }

        return records;
    }

    // {{{Y,M,D},{h,m,s}}, {{Y,M,D},{h,m,s}}} per window: twelve integers per start/end pair.
    private static List<Dictionary<string, string>> ParseStateWindows(string table)
    {
        var numbers = Regex.Matches(table, @"-?\d+", RegexOptions.CultureInvariant)
            .Select(match => SafeInt(match.Value))
            .ToArray();
        var windows = new List<Dictionary<string, string>>();
        for (var index = 0; index + 12 <= numbers.Length; index += 12)
        {
            windows.Add(new Dictionary<string, string>
            {
                ["start"] = FormatServerTime(numbers.AsSpan(index, 6)),
                ["end"] = FormatServerTime(numbers.AsSpan(index + 6, 6))
            });
        }

        return windows;
    }

    private static string FormatServerTime(ReadOnlySpan<int> parts)
    {
        try
        {
            return new DateTime(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], DateTimeKind.Unspecified)
                .ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "";
        }
    }

    // description = { {"skill name", grade, "text"}, ... }; the grade's meaning is not documented.
    private static List<Dictionary<string, object?>> ParseBossSkills(string table)
    {
        const string text = "\"(?<{0}>(?:\\\\.|[^\"\\\\])*)\"";
        var pattern = new Regex(
            @"\{\s*" + string.Format(CultureInfo.InvariantCulture, text, "name") + @"\s*,\s*(?<grade>-?\d+)\s*,\s*" +
            string.Format(CultureInfo.InvariantCulture, text, "text") + @"\s*\}",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return pattern.Matches(table)
            .Select(match => new Dictionary<string, object?>
            {
                ["name"] = UnescapeLuaString(match.Groups["name"].Value),
                ["grade"] = SafeInt(match.Groups["grade"].Value),
                ["text"] = StripRichText(UnescapeLuaString(match.Groups["text"].Value))
            })
            .ToList();
    }

    // Skill text highlights phrases with Unity rich-text tags such as <color=#92fc63>; the app shows plain text.
    private static string StripRichText(string value) =>
        Regex.Replace(value, @"</?color(=[^>]*)?>", "", RegexOptions.CultureInvariant).Trim();

    private static string UnescapeLuaString(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string>($"\"{value}\"") ?? value;
        }
        catch (JsonException)
        {
            return value.Replace("\\\"", "\"", StringComparison.Ordinal);
        }
    }

    private static Dictionary<string, Dictionary<string, object?>> BuildMetaBossMap(
        IReadOnlyList<Dictionary<string, object?>> bosses,
        IReadOnlyList<Dictionary<string, object?>> bossLevels,
        string enemyStatisticsPath,
        string dungeonDirectory)
    {
        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        if (bosses.Count == 0)
        {
            return result;
        }

        var levelsById = bossLevels
            .GroupBy(row => SafeInt(Get(row, "id")))
            .ToDictionary(group => group.Key, group => group.First());
        // Each boss owns the level rows from its boss_level_id up to the next boss's first row.
        var starts = bosses.Select(boss => SafeInt(Get(boss, "boss_level_id"))).Where(id => id > 0).Distinct()
            .Order().ToList();
        var lastLevelId = levelsById.Keys.DefaultIfEmpty(0).Max();
        var enemies = new LuaRecordIndex(enemyStatisticsPath, "enemy_data_statistics");

        foreach (var boss in bosses.OrderBy(boss => SafeInt(Get(boss, "id"))))
        {
            var id = SafeInt(Get(boss, "id"));
            var first = SafeInt(Get(boss, "boss_level_id"));
            if (id == 0 || first == 0)
            {
                continue;
            }

            var next = starts.FirstOrDefault(start => start > first);
            var last = next > 0 ? next - 1 : lastLevelId;
            var levels = new List<Dictionary<string, object?>>();
            for (var levelId = first; levelId <= last; levelId++)
            {
                if (!levelsById.TryGetValue(levelId, out var row))
                {
                    continue;
                }

                levels.Add(new Dictionary<string, object?>
                {
                    ["level"] = levelId - first + 1,
                    ["hp"] = SafeInt(Get(row, "hp")),
                    ["enemy_id"] = SafeInt(Get(row, "enemy_id")),
                    ["expedition_id"] = SafeInt(Get(row, "expedition_id"))
                });
            }

            // Armor and hull come from the strongest level's enemy (or its base template).
            var top = levels.LastOrDefault();
            var enemy = enemies.Resolve(SafeInt(Get(top, "enemy_id")), ["armor_type", "type"]);
            var armorType = SafeInt(Get(enemy, "armor_type"));
            var enemyType = SafeInt(Get(enemy, "type"));
            var state = Get(boss, "state") as string;
            result[id.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["id"] = id,
                ["name"] = Convert.ToString(Get(boss, "name"), CultureInfo.InvariantCulture)?.Trim() ?? "",
                ["meta_id"] = SafeInt(Get(boss, "meta_id")),
                ["boss_level_id"] = first,
                ["availability"] = state is "always" or "stop" ? state : "timed",
                ["windows"] = Get(boss, "windows") ?? new List<Dictionary<string, string>>(),
                ["armor_type"] = armorType,
                ["armor"] = ArmorNames.GetValueOrDefault(armorType, ""),
                ["enemy_type"] = enemyType,
                ["hull"] = EnemyHulls.GetValueOrDefault(enemyType, ""),
                ["battle_seconds"] = DungeonTimeLimit(dungeonDirectory, SafeInt(Get(top, "expedition_id"))),
                ["levels"] = levels,
                ["skills"] = Get(boss, "skills") ?? new List<Dictionary<string, object?>>()
            };
        }

        return result;
    }

    private static int DungeonTimeLimit(string dungeonDirectory, int expeditionId)
    {
        var path = Path.Combine(dungeonDirectory, $"{expeditionId.ToString(CultureInfo.InvariantCulture)}.lua");
        if (expeditionId == 0 || !File.Exists(path))
        {
            return 0;
        }

        var match = Regex.Match(File.ReadAllText(path), @"\btimeCount\s*=\s*(\d+)", RegexOptions.CultureInvariant);
        return match.Success ? SafeInt(match.Groups[1].Value) : 0;
    }

    /// <summary>
    /// Looks up single records in a large Lua table file without parsing all of it, following
    /// <c>base</c> references until the wanted fields are found.
    /// </summary>
    private sealed class LuaRecordIndex(string path, string tableName)
    {
        private readonly string text = File.Exists(path) ? File.ReadAllText(path) : "";

        public Dictionary<string, object?> Resolve(int id, string[] fields)
        {
            var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
            var seen = new HashSet<int>();
            while (id > 0 && seen.Add(id))
            {
                var record = Find(id, [.. fields, "base"]);
                foreach (var field in fields)
                {
                    if (!merged.ContainsKey(field) && record.TryGetValue(field, out var value))
                    {
                        merged[field] = value;
                    }
                }

                if (fields.All(merged.ContainsKey))
                {
                    break;
                }

                id = SafeInt(Get(record, "base"));
            }

            return merged;
        }

        private Dictionary<string, object?> Find(int id, string[] fields)
        {
            var marker = $"pg.base.{tableName}[{id.ToString(CultureInfo.InvariantCulture)}] = {{";
            var start = text.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return [];
            }

            var openIndex = start + marker.Length - 1;
            var block = text.Substring(openIndex, FindLuaTableEnd(text, openIndex) - openIndex + 1);
            return ParseLuaRecordBlock(block, fields);
        }
    }
}
