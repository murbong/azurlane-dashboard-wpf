using System.Globalization;
using System.Text.Json;

namespace AzurLaneDashboard.Core.Services;

/// <summary>Cognitive awakening cost charged at a level cap.</summary>
public sealed record AwakeningCost(int Gold, int Units, int UnitsII)
{
    public const int UnitItemId = 15008;
    public const int UnitIIItemId = 15012;
}

/// <summary>
/// Level caps from maps/ship_level.json: rows with <c>level_limit = 1</c> (100, 105, ...) stop a ship until
/// it is awakened; <c>awaken</c> holds the cost per rarity code (2 = N ... 6 = UR/DR).
/// </summary>
public sealed class ShipLevelCaps
{
    private readonly JsonElement levels;
    private readonly IReadOnlyList<int> caps;
    private long[]? cumulative;
    private long[]? cumulativeRare;

    public ShipLevelCaps(JsonElement levels)
    {
        this.levels = levels;
        caps = levels.ValueKind != JsonValueKind.Object
            ? []
            : levels.EnumerateObject()
                .Where(row => row.Value.ValueKind == JsonValueKind.Object && Int(row.Value, "level_limit") == 1)
                .Select(row => Int(row.Value, "level"))
                .Where(level => level > 0)
                .Order()
                .ToList();
    }

    public IReadOnlyList<int> Caps => caps;

    public int Required(int level, string rarity) => ShipExperienceCalculator.Required(levels, level, rarity);

    /// <summary>EXP earned since Lv.1: every level below <paramref name="level"/> plus the EXP inside it.</summary>
    public long Total(int level, int experience, string rarity)
    {
        var table = RarityHelper.Normalize(rarity) is "UR" or "DR"
            ? cumulativeRare ??= Cumulative(rarity)
            : cumulative ??= Cumulative(rarity);
        return table[Math.Clamp(level, 1, ShipExperienceCalculator.MaximumLevel)] + Math.Max(0, experience);
    }

    private long[] Cumulative(string rarity)
    {
        var table = new long[ShipExperienceCalculator.MaximumLevel + 1];
        for (var level = 2; level < table.Length; level++)
            table[level] = table[level - 1] + Math.Max(0, Required(level - 1, rarity));
        return table;
    }

    /// <summary>EXP a ship can bank at its max level (gameset exp_overflow_max).</summary>
    public const int OverflowMax = 3_000_000;

    /// <summary>
    /// The level a ship cannot pass without the player: its template cap (70/80/90) until fully limit broken,
    /// else the first cap at or above its level. A ship standing on a cap is taken as not awakened past it,
    /// as <see cref="IsWaitingForAwakening"/> does.
    /// </summary>
    public int MaxLevel(int level, int templateMaxLevel) =>
        templateMaxLevel is > 0 and < 100 && level <= templateMaxLevel
            ? templateMaxLevel
            : caps.FirstOrDefault(cap => cap >= level, ShipExperienceCalculator.MaximumLevel);

    /// <summary>The cap a level-cap break (CS 12038) raises <paramref name="maxLevel"/> to, as Ship:getNextMaxLevel.</summary>
    public int NextCap(int maxLevel) => caps.FirstOrDefault(cap => cap > maxLevel, ShipExperienceCalculator.MaximumLevel);

    /// <summary>
    /// The client's Ship.addExp: levels up while the EXP covers the level and stops at the ship's max level
    /// (<paramref name="maxLevel"/> from its packet, else <see cref="MaxLevel"/>). A battle result banks the rest
    /// there up to <see cref="OverflowMax"/>; EXP books, the courtyard and commissions
    /// (<paramref name="banksOverflow"/> false) give nothing at the cap and drop the rest on reaching it.
    /// </summary>
    public (int Level, int Experience) AddExperience(
        int level, int experience, int gained, string rarity, int templateMaxLevel, bool banksOverflow = true,
        int maxLevel = 0)
    {
        var max = maxLevel > 0 ? maxLevel : MaxLevel(level, templateMaxLevel);
        long exp = Math.Max(0, experience);
        if (level >= max)
        {
            if (!banksOverflow) return (level, experience);
            return (level, exp >= OverflowMax ? (int)exp : (int)Math.Min(OverflowMax, exp + Math.Max(0, gained)));
        }

        exp += Math.Max(0, gained);
        while (level < max)
        {
            var required = Required(level, rarity);
            if (required <= 0 || exp < required) break;
            exp -= required;
            level++;
        }

        if (level < max) return (level, (int)exp);
        return (level, banksOverflow ? (int)Math.Min(exp, OverflowMax) : 0);
    }

    /// <summary>
    /// The level the ship stops at next. A ship standing on a cap with banked EXP for the next level is
    /// waiting for awakening at that cap; otherwise the next cap above it. Ships that are not fully
    /// limit broken stop at their template's max level (70/80/90) first.
    /// </summary>
    public int NextStop(int level, int experience, string rarity, int templateMaxLevel)
    {
        if (templateMaxLevel is > 0 and < 100 && level <= templateMaxLevel) return templateMaxLevel;
        if (IsWaitingForAwakening(level, experience, rarity)) return level;
        return caps.FirstOrDefault(cap => cap > level, ShipExperienceCalculator.MaximumLevel);
    }

    public bool IsWaitingForAwakening(int level, int experience, string rarity)
    {
        if (!caps.Contains(level) || level >= ShipExperienceCalculator.MaximumLevel) return false;
        var required = Required(level, rarity);
        return required > 0 && experience >= required;
    }

    public AwakeningCost? Awakening(int capLevel, string rarity)
    {
        if (levels.ValueKind != JsonValueKind.Object ||
            !levels.TryGetProperty(capLevel.ToString(CultureInfo.InvariantCulture), out var row) ||
            !row.TryGetProperty("awaken", out var awaken) || awaken.ValueKind != JsonValueKind.Object ||
            !awaken.TryGetProperty(RarityCode(rarity).ToString(CultureInfo.InvariantCulture), out var cost))
            return null;

        return new AwakeningCost(Int(cost, "gold"), Int(cost, AwakeningCost.UnitItemId.ToString(CultureInfo.InvariantCulture)),
            Int(cost, AwakeningCost.UnitIIItemId.ToString(CultureInfo.InvariantCulture)));
    }

    public static int RarityCode(string rarity) => RarityHelper.Normalize(rarity) switch
    {
        "UR" or "DR" => 6,
        "SSR" or "PR" => 5,
        "SR" => 4,
        "R" => 3,
        _ => 2
    };

    private static int Int(JsonElement row, string key) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
}
