using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>Reads maps/meta_bosses.json and maps/meta_boss_guides.json and answers which boss is up.</summary>
public static class MetaBossCatalog
{
    // Lua state windows are the KR server's wall clock (assumed UTC+9; to be confirmed against a rotation change).
    private static readonly TimeSpan ServerOffset = TimeSpan.FromHours(9);

    public static DateTime ServerTime(DateTimeOffset now) => now.ToOffset(ServerOffset).DateTime;

    public static IReadOnlyList<MetaBoss> Parse(JsonElement map)
    {
        if (map.ValueKind != JsonValueKind.Object) return [];
        return map.EnumerateObject()
            .Select(property => property.Value)
            .Where(row => row.ValueKind == JsonValueKind.Object)
            .Select(row => new MetaBoss
            {
                Id = Int(row, "id"),
                Name = Text(row, "name"),
                MetaId = Int(row, "meta_id"),
                Availability = Text(row, "availability"),
                Windows = Array(row, "windows").Select(window => new MetaBossWindow(
                        Time(Text(window, "start")), Time(Text(window, "end"))))
                    .Where(window => window.Start != default && window.End != default)
                    .ToList(),
                Armor = Text(row, "armor"),
                Hull = Text(row, "hull"),
                BattleSeconds = Int(row, "battle_seconds"),
                Levels = Array(row, "levels").Select(level => new MetaBossLevel(
                    Int(level, "level"), Int(level, "hp"), Int(level, "enemy_id"), Int(level, "expedition_id"))).ToList(),
                Skills = Array(row, "skills").Select(skill => new MetaBossSkill(
                    Text(skill, "name"), Int(skill, "grade"), Text(skill, "text"))).ToList()
            })
            .Where(boss => boss.Id > 0)
            .OrderBy(boss => boss.Id)
            .ToList();
    }

    /// <summary>The rotation boss; like the client, the last active window in template order wins.</summary>
    public static MetaBoss? Current(IEnumerable<MetaBoss> bosses, DateTime serverTime) =>
        bosses.Where(boss => boss.IsActive(serverTime)).OrderBy(boss => boss.Id).LastOrDefault();

    /// <summary>What the player can fight now: the rotation boss, then the archive.</summary>
    public static IReadOnlyList<MetaBoss> Selectable(IReadOnlyList<MetaBoss> bosses, DateTime serverTime)
    {
        var current = Current(bosses, serverTime);
        return (current is null ? [] : new[] { current })
            .Concat(bosses.Where(boss => boss.IsArchive && boss.Id != current?.Id))
            .ToList();
    }

    /// <summary>Saved choice when still selectable, else the rotation boss, the game's archive pick, the first archive boss.</summary>
    public static MetaBoss? DefaultSelection(
        IReadOnlyList<MetaBoss> bosses, DateTime serverTime, int savedBossId = 0, int gameArchiveBossId = 0)
    {
        var selectable = Selectable(bosses, serverTime);
        return selectable.FirstOrDefault(boss => boss.Id == savedBossId && savedBossId > 0)
               ?? Current(bosses, serverTime)
               ?? selectable.FirstOrDefault(boss => boss.Id == gameArchiveBossId)
               ?? selectable.FirstOrDefault();
    }

    public static (IReadOnlyDictionary<int, MetaBossGuide> Guides, IReadOnlyList<RegalStarRule> StarRules) ParseGuides(
        JsonElement map)
    {
        var guides = new Dictionary<int, MetaBossGuide>();
        if (map.ValueKind == JsonValueKind.Object && map.TryGetProperty("bosses", out var bosses) &&
            bosses.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in bosses.EnumerateObject())
            {
                if (!int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
                    property.Value.ValueKind != JsonValueKind.Object)
                    continue;
                var row = property.Value;
                var fleet = row.TryGetProperty("fleet", out var value) ? value : default;
                guides[id] = new MetaBossGuide
                {
                    BossId = id,
                    Verified = row.TryGetProperty("verified", out var verified) && verified.ValueKind == JsonValueKind.True,
                    LastReviewed = Text(row, "lastReviewed"),
                    ArmorAlternates = Strings(row, "armorAlternates"),
                    ArmorPeriodSeconds = Int(row, "armorPeriodSeconds"),
                    Mechanics = Array(row, "mechanics").Select(item => new MetaBossMechanic(
                        Text(item, "title"), Text(item, "text"), Text(item, "response"))).ToList(),
                    Main = Array(fleet, "main").Select(StringList).ToList(),
                    Vanguard = Array(fleet, "vanguard").Select(StringList).ToList(),
                    FlagshipIndex = Int(fleet, "flagship"),
                    Notes = Text(fleet, "notes"),
                    Sources = Array(row, "sources").Select(item => new MetaBossSource(
                        Text(item, "title"), Text(item, "url"), Text(item, "sourceUpdated"))).ToList()
                };
            }
        }

        var rules = Array(map, "regalStarRules")
            .Select(item => new RegalStarRule(Text(item, "regalUid"), Strings(item, "hulls"), Strings(item, "armors")))
            .Where(rule => rule.RegalUid.Length > 0)
            .ToList();
        return (guides, rules);
    }

    /// <summary>Armors the boss fights in: the guide's alternation when verified, else the game data's armor.</summary>
    public static IReadOnlyList<string> Armors(MetaBoss boss, MetaBossGuide? guide) =>
        guide is { Verified: true, ArmorAlternates.Count: > 0 } ? guide.ArmorAlternates
        : boss.Armor.Length > 0 ? [boss.Armor] : [];

    public static IReadOnlyList<string> DamageMetrics(IReadOnlyList<string> armors) =>
        armors.Count == 0 ? ["Heavy Dmg"] : armors.Select(armor => $"{armor} Dmg").ToList();

    /// <summary>Regal gear-map usecase: L/M/H by armor; an alternating boss uses its starting armor.</summary>
    public static string GearUsecase(IReadOnlyList<string> armors) => armors.FirstOrDefault() switch
    {
        "Light" => "L",
        "Medium" => "M",
        _ => "H"
    };

    public static string ArmorLabel(string armor) => armor switch
    {
        "Light" => "경장갑",
        "Medium" => "중형",
        "Heavy" => "중장갑",
        _ => "-"
    };

    private static DateTime Time(string value) =>
        DateTime.TryParseExact(value, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None,
            out var time) ? time : default;

    private static IReadOnlyList<string> StringList(JsonElement element) =>
        element.ValueKind != JsonValueKind.Array
            ? []
            : element.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? "").Where(item => item.Length > 0).ToList();

    private static IReadOnlyList<string> Strings(JsonElement row, string key) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) ? StringList(value) : [];

    private static IEnumerable<JsonElement> Array(JsonElement row, string key) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    private static string Text(JsonElement row, string key) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int Int(JsonElement row, string key) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;
}
