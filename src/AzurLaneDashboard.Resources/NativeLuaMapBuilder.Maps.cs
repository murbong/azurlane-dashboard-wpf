using System.Globalization;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    private static Dictionary<string, Dictionary<string, object?>> BuildShipMap(
        IReadOnlyList<Dictionary<string, object?>> shipStats,
        IReadOnlyList<Dictionary<string, object?>> shipTemplates)
    {
        var templatesById = shipTemplates
            .Where(item => SafeInt(Get(item, "id")) != 0)
            .ToDictionary(item => SafeInt(Get(item, "id")), item => item);
        var groups = shipStats
            .GroupBy(ship =>
                Convert.ToString(
                    FirstNonEmpty(Get(ship, "skin_id"), Get(ship, "english_name"), Get(ship, "name"), Get(ship, "id")),
                    CultureInfo.InvariantCulture) ?? "")
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key, group => group.ToList());
        var minStarByGroup = groups.ToDictionary(
            group => group.Key,
            group =>
            {
                var stars = group.Value.Select(ship => SafeInt(Get(ship, "star"))).Where(star => star != 0).ToList();
                return stars.Count == 0 ? 0 : stars.Min();
            });

        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var ship in shipStats)
        {
            var shipId = SafeInt(Get(ship, "id"));
            if (shipId == 0)
            {
                continue;
            }

            templatesById.TryGetValue(shipId, out var template);
            template ??= new Dictionary<string, object?>();
            var groupKey =
                Convert.ToString(
                    FirstNonEmpty(Get(ship, "skin_id"), Get(ship, "english_name"), Get(ship, "name"), shipId),
                    CultureInfo.InvariantCulture) ?? shipId.ToString(CultureInfo.InvariantCulture);
            var star = SafeInt(Get(ship, "star"));
            var baseStar = minStarByGroup.TryGetValue(groupKey, out var minStar) ? minStar : star;
            result[shipId.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["id"] = shipId,
                ["name"] = Convert.ToString(FirstNonEmpty(Get(ship, "name"), Get(ship, "english_name")),
                    CultureInfo.InvariantCulture) ?? "",
                ["english_name"] = Convert.ToString(Get(ship, "english_name"), CultureInfo.InvariantCulture) ?? "",
                ["rarity"] = Get(ship, "rarity") ?? "",
                ["star"] = star,
                ["base_star"] = baseStar,
                ["limit_break"] = Math.Max(0, star - baseStar),
                ["group_type"] = SafeInt(FirstNonEmpty(Get(template, "group_type"), Get(ship, "group_type"))),
                ["star_max"] = SafeInt(Get(template, "star_max")),
                ["max_level"] = SafeInt(Get(template, "max_level")),
                ["skin_id"] = Get(ship, "skin_id") ?? "",
                ["ship_type"] = Get(ship, "type") ?? "",
                ["nationality"] = Get(ship, "nationality") ?? ""
            };
        }

        return result;
    }

    private static Dictionary<string, Dictionary<string, object?>> BuildSkinMap(
        IReadOnlyList<Dictionary<string, object?>> skins)
    {
        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var skin in skins)
        {
            var skinId = SafeInt(Get(skin, "id"));
            if (skinId == 0)
            {
                continue;
            }

            result[skinId.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["id"] = skinId,
                ["name"] = Get(skin, "name") ?? "",
                ["ship_group"] = Get(skin, "ship_group") ?? "",
                ["skin_type"] = Get(skin, "skin_type") ?? "",
                ["shop_id"] = Get(skin, "shop_id") ?? "",
                ["prefab"] = Get(skin, "prefab") ?? "",
                ["painting"] = Get(skin, "painting") ?? "",
                ["icon_url"] = "",
                ["qicon_url"] = "",
                ["painting_url"] = "",
                ["shipyard_url"] = ""
            };
        }

        return result;
    }
}
