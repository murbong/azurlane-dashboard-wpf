using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static JsonElement ResolveFleetTech(int templateId, JsonElement shipInfo, int skinId,
        JsonElement fleetTechMap)
    {
        foreach (var candidate in new[] { JsonInt(shipInfo, "group_type"), templateId / 10, templateId, skinId / 10 })
        {
            if (candidate > 0)
            {
                var row = MapObject(fleetTechMap, candidate);
                if (row.ValueKind == JsonValueKind.Object)
                {
                    return row;
                }
            }
        }

        return default;
    }

    private static string FleetBonusText(JsonElement tech, string prefix)
    {
        if (tech.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        var attr = JsonInt(tech, $"{prefix}_attr");
        var value = JsonInt(tech, $"{prefix}_value");
        if (attr <= 0 || value <= 0)
        {
            return "";
        }

        var shipTypes = Array(tech, $"{prefix}_shiptypes").Select(item => ShipTypeLabel(IntValue(item)))
            .Where(label => !string.IsNullOrWhiteSpace(label)).Take(3);
        var scope = string.Join("/", shipTypes);
        return $"{(string.IsNullOrWhiteSpace(scope) ? "전체" : scope)} {AttrLabel(attr)} +{value}";
    }

    private static (int Current, int Required, double Percent, bool IsMax) LevelProgress(int level, int exp,
        string rarity, JsonElement levelMap)
    {
        var required = LevelExpRequired(level, rarity, levelMap);
        if (level >= 125)
        {
            return (exp, required, 100, true);
        }

        return (exp, required, Math.Round(Math.Clamp(required == 0 ? 0 : exp * 100.0 / required, 0, 100), 1), false);
    }

    private static (int Level, int Exp) AddShipExp(int level, int exp, int gained, string rarity, JsonElement levelMap)
    {
        var nextLevel = Math.Max(1, level);
        var nextExp = Math.Max(0, exp) + Math.Max(0, gained);
        while (nextLevel > 0)
        {
            var required = LevelExpRequired(nextLevel, rarity, levelMap);
            if (required <= 0 || nextExp < required || LevelIsLimit(nextLevel, levelMap))
            {
                break;
            }

            nextExp -= required;
            nextLevel++;
        }

        return (nextLevel, nextExp);
    }

    private static int? ExpRemainingToLevel(int level, int exp, int target, string rarity, JsonElement levelMap)
        => ShipExperienceCalculator.Remaining(level, exp, target,
            next => LevelExpRequired(next, rarity, levelMap));

    private static int LevelExpRequired(int level, string rarity, JsonElement levelMap)
        => ShipExperienceCalculator.Required(levelMap, level, rarity);

    private static bool LevelIsLimit(int level, JsonElement levelMap) =>
        JsonInt(MapObject(levelMap, level), "level_limit") == 1;

    private static string EquippedSummary(List<EquipmentItemState> items)
    {
        var names = items.Where(item => !item.Empty)
            .Select(item => item.EnhanceLevel > 0 ? $"{item.Name} +{item.EnhanceLevel}" : item.Name).ToList();
        var empty = items.Count(item => item.Empty);
        if (empty > 0)
        {
            names.Add($"빈칸 {empty}");
        }

        return names.Count == 0 ? "-" : string.Join(" · ", names);
    }

    private static int EquipmentReloadBonus(IEnumerable<EquipmentItemState> items) =>
        items.Where(item => !item.Empty).Sum(item => item.ReloadBonus);

    private static int EquipmentReloadBonus(IEnumerable<EquipmentAttributeRow> attributes) =>
        attributes
            .Where(attribute => string.Equals(attribute.Type, "reload", StringComparison.OrdinalIgnoreCase))
            .Sum(attribute => attribute.Value);

    private static IReadOnlyList<EquipmentAttributeRow> EquipmentAttributes(
        JsonElement info,
        int templateId,
        IReadOnlyDictionary<int, IReadOnlyList<EquipmentAttributeRow>> equipmentAttributes)
    {
        var mapped = EquipmentAttributes(info);
        if (mapped.Count > 0)
        {
            return mapped;
        }

        if (templateId > 0 && equipmentAttributes.TryGetValue(templateId, out var attributes))
        {
            return attributes;
        }

        var baseId = JsonInt(info, "base");
        return baseId > 0 && equipmentAttributes.TryGetValue(baseId, out attributes) ? attributes : [];
    }

    private static IReadOnlyList<EquipmentAttributeRow> EquipmentAttributes(JsonElement info)
    {
        if (info.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var structured = Array(info, "attributes")
            .Select(attribute => new EquipmentAttributeRow(
                JsonInt(attribute, "index"),
                JsonString(attribute, "type"),
                FirstNonEmpty(JsonString(attribute, "label"),
                    EquipmentAttributeRow.LabelFor(JsonString(attribute, "type"))),
                JsonInt(attribute, "value")))
            .Where(attribute => attribute.Index > 0 &&
                                !string.IsNullOrWhiteSpace(attribute.Type) &&
                                attribute.Value != 0)
            .ToArray();
        if (structured.Length > 0)
        {
            return structured;
        }

        var legacy = new List<EquipmentAttributeRow>();
        for (var index = 1; index <= 3; index++)
        {
            var type = JsonString(info, $"attribute_{index}");
            var value = JsonInt(info, $"value_{index}");
            if (!string.IsNullOrWhiteSpace(type) && value != 0)
            {
                legacy.Add(new EquipmentAttributeRow(index, type, EquipmentAttributeRow.LabelFor(type), value));
            }
        }

        return legacy;
    }

    private static int SkillGap(List<SkillState> skills, JsonElement skillMap)
    {
        var total = 0;
        foreach (var skill in skills)
        {
            var info = MapObject(skillMap, skill.Id);
            var max = JsonInt(info, "max_level");
            if (max <= 1 && info.ValueKind == JsonValueKind.Object)
            {
                continue;
            }

            var target = max > 1 ? max : 10;
            total += Math.Max(0, target - skill.Level);
        }

        return total;
    }

    private static bool IsBulin(int templateId, string name)
    {
        return templateId is 100001 or 100011 or 100021
               || name.Contains("부린", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Bulin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDockExempt(ShipState ship) =>
        ship.NationId == 97 || ship.Rarity == "META" || !ship.HasFullState;

    private static string EquipmentSkinName(int skinId, JsonElement skinMap)
    {
        if (skinId <= 0)
        {
            return "";
        }

        var info = MapObject(skinMap, skinId);
        return FirstNonEmpty(JsonString(info, "name"), $"skin:{skinId}");
    }

    private static string RarityTier(int rarity)
    {
        return rarity switch
        {
            >= 6 => "UR",
            5 => "SSR",
            4 => "SR",
            3 => "R",
            _ => "N"
        };
    }

    private static bool RetrofitComplete(IEnumerable<int>? transformIds, int groupId, JsonElement transformMap)
    {
        var record = MapObject(transformMap, groupId);
        var required = ArrayItems(Child(record, "transform_ids"))
            .Select(IntValue)
            .Where(value => value > 0)
            .ToHashSet();
        if (required.Count == 0 || transformIds is null)
        {
            return false;
        }

        var owned = transformIds.Where(value => value > 0).ToHashSet();
        return required.IsSubsetOf(owned);
    }

    private static string ShipDisplayRarity(
        int baseRarity,
        int templateId,
        int groupId,
        JsonElement blueprintMap,
        bool isRetrofitted)
    {
        if (baseRarity <= 0)
        {
            return "";
        }

        var baseTier = RarityTier(baseRarity);
        var displayTier = isRetrofitted
            ? baseTier switch
            {
                "N" => "R",
                "R" => "SR",
                "SR" => "SSR",
                "SSR" => "UR",
                "UR" => "UR",
                _ => baseTier
            }
            : baseTier;

        if (IsResearchShip(templateId, groupId, blueprintMap))
        {
            return displayTier switch
            {
                "UR" => "DR",
                "SSR" => "PR",
                _ => displayTier
            };
        }

        return displayTier;
    }

    private static bool IsResearchShip(int templateId, int groupId, JsonElement blueprintMap)
    {
        return HasMapKey(blueprintMap, groupId) ||
               HasMapKey(blueprintMap, templateId) ||
               HasMapKey(blueprintMap, templateId / 10);
    }
}
