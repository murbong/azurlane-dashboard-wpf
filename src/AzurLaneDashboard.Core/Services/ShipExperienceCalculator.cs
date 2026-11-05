using System.Globalization;
using System.Text.Json;

namespace AzurLaneDashboard.Core.Services;

/// <summary>Shared by snapshot estimates and user-defined growth goals.</summary>
public static class ShipExperienceCalculator
{
    public const int MaximumLevel = 125;

    public static int Required(JsonElement levels, int level, string rarity)
    {
        if (levels.ValueKind != JsonValueKind.Object ||
            !levels.TryGetProperty(level.ToString(CultureInfo.InvariantCulture), out var row) ||
            row.ValueKind != JsonValueKind.Object)
            return 0;

        static int Read(JsonElement row, string key) =>
            row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var number) ? number : 0;

        var ordinary = Read(row, "exp");
        var rare = Read(row, "exp_ur");
        return RarityHelper.Normalize(rarity) is "UR" or "DR" && rare > 0 ? rare : ordinary;
    }

    public static int? Remaining(int level, int experience, int target, Func<int, int> requiredAtLevel)
    {
        if (level is < 1 or > MaximumLevel || target is < 1 or > MaximumLevel)
            return null;
        if (level >= target)
            return 0;

        long total = 0;
        for (var next = level; next < target; next++)
        {
            var required = requiredAtLevel(next);
            // Missing metadata must never look like free levels or a completed goal.
            if (required <= 0)
                return null;
            total += required;
        }

        // Banked EXP at a level cap applies to subsequent levels as well.
        total = Math.Max(0, total - Math.Max(0, experience));
        return total <= int.MaxValue ? (int)total : null;
    }
}
