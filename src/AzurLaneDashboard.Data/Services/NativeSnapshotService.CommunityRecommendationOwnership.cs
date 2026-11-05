using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static Dictionary<string, JsonElement> BuildLocalAliasMap(JsonElement map)
    {
        var aliases = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (map.ValueKind != JsonValueKind.Object)
        {
            return aliases;
        }

        foreach (var property in map.EnumerateObject())
        {
            var info = property.Value;
            foreach (var alias in AliasIds(
                         ToInt(property.Name),
                         JsonInt(info, "id"),
                         JsonInt(info, "template_id"),
                         JsonInt(info, "base"),
                         JsonInt(info, "icon_id")))
            {
                if (!aliases.TryGetValue(alias, out var current) ||
                    JsonInt(info, "level") > JsonInt(current, "level"))
                {
                    aliases[alias] = info;
                }
            }
        }

        return aliases;
    }

    private static Dictionary<string, List<OwnedRecommendationItem>> BuildOwnedEquipmentLookup(
        List<EquipmentRow> equipment,
        JsonElement equipmentMap)
    {
        var lookup = new Dictionary<string, List<OwnedRecommendationItem>>(StringComparer.Ordinal);
        foreach (var row in equipment)
        {
            var templateId = ToInt(row.TemplateId);
            if (templateId <= 0)
            {
                continue;
            }

            var info = MapObject(equipmentMap, templateId);
            var owned = new OwnedRecommendationItem
            {
                TemplateId = templateId,
                Name = row.Name,
                Count = row.Count,
                EnhanceLevel = row.EnhanceLevel,
                RarityScore = RarityScore(row.Rarity)
            };

            AddOwnedAliases(lookup, owned, AliasIds(
                templateId,
                JsonInt(info, "id"),
                JsonInt(info, "template_id"),
                JsonInt(info, "base"),
                JsonInt(info, "icon_id")));
        }

        SortOwnedLookup(lookup);
        return lookup;
    }

    private static Dictionary<string, List<OwnedRecommendationItem>> BuildOwnedSpecialEquipmentLookup(
        List<SpecialEquipmentRow> specialEquipment,
        JsonElement spWeaponMap)
    {
        var lookup = new Dictionary<string, List<OwnedRecommendationItem>>(StringComparer.Ordinal);
        foreach (var row in specialEquipment)
        {
            var templateId = ToInt(row.TemplateId);
            if (templateId <= 0)
            {
                continue;
            }

            var info = MapObject(spWeaponMap, templateId);
            var owned = new OwnedRecommendationItem
            {
                TemplateId = templateId,
                Name = row.Name,
                Count = 1,
                EnhanceLevel = row.EnhanceLevel,
                RarityScore = RarityScore(row.Rarity)
            };

            AddOwnedAliases(lookup, owned, AliasIds(
                templateId,
                JsonInt(info, "id"),
                JsonInt(info, "template_id"),
                JsonInt(info, "base"),
                JsonInt(info, "icon_id")));
        }

        SortOwnedLookup(lookup);
        return lookup;
    }

    private static void AddOwnedAliases(
        Dictionary<string, List<OwnedRecommendationItem>> lookup,
        OwnedRecommendationItem item,
        IEnumerable<string> aliases)
    {
        foreach (var alias in aliases)
        {
            if (!lookup.TryGetValue(alias, out var rows))
            {
                rows = new List<OwnedRecommendationItem>();
                lookup[alias] = rows;
            }

            rows.Add(item);
        }
    }

    private static void SortOwnedLookup(Dictionary<string, List<OwnedRecommendationItem>> lookup)
    {
        foreach (var rows in lookup.Values)
        {
            rows.Sort((a, b) =>
            {
                var enhance = b.EnhanceLevel.CompareTo(a.EnhanceLevel);
                if (enhance != 0) return enhance;
                var rarity = b.RarityScore.CompareTo(a.RarityScore);
                if (rarity != 0) return rarity;
                return b.Count.CompareTo(a.Count);
            });
        }
    }

    private static List<string> AliasIds(params int[] values)
    {
        var result = new List<string>();
        var seen = new HashSet<int>();
        foreach (var value in values)
        {
            if (value <= 0 || !seen.Add(value))
            {
                continue;
            }

            result.Add(value.ToString(CultureInfo.InvariantCulture));
        }

        return result;
    }
}