using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static void AttachCommunityRecommendations(
        List<ShipState> ships,
        SnapshotMaps maps,
        List<EquipmentRow> equipment,
        List<SpecialEquipmentRow> specialEquipment,
        string projectRoot)
    {
        if (maps.RegalShips.ValueKind != JsonValueKind.Array ||
            maps.RegalGearMap.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var regalIndex = BuildRegalShipIndex(maps.RegalShips);
        var equipmentAliases = BuildLocalAliasMap(maps.Equipment);
        var spWeaponAliases = BuildLocalAliasMap(maps.SpWeapons);
        var ownedEquipment = BuildOwnedEquipmentLookup(equipment, maps.Equipment);

        var ownedSpecialItems = new List<SpecialEquipmentRow>(specialEquipment);
        foreach (var ship in ships)
        {
            if (ship.SpecialEquipment is not null)
            {
                ownedSpecialItems.Add(ship.SpecialEquipment);
            }
        }

        var ownedSpecialEquipment = BuildOwnedSpecialEquipmentLookup(ownedSpecialItems, maps.SpWeapons);
        // JsonElement property lookups scan every property; with ~900 ships each walking hundreds of gear-map
        // keys that dominated the snapshot. The sections and item maps are indexed once per load instead.
        var gearIndex = new RegalGearIndex(maps.RegalGearMap);
        var gearItems = IndexObject(maps.RegalEquips);
        var augmentItems = IndexObject(maps.RegalAugments);

        foreach (var ship in ships)
        {
            var regal = MatchRegalShip(ship, regalIndex);
            if (regal.ValueKind == JsonValueKind.Undefined)
            {
                continue;
            }

            var gearRecommendations = BuildRegalRecommendations(
                JsonInt(regal, "gear"),
                gearIndex,
                gearItems,
                equipmentAliases,
                ownedEquipment,
                "gear",
                projectRoot,
                120);
            var augmentRecommendations = BuildRegalRecommendations(
                JsonInt(regal, "aug"),
                gearIndex,
                augmentItems,
                spWeaponAliases,
                ownedSpecialEquipment,
                "augment",
                projectRoot,
                80);

            var gearSummary = RecommendationSummary(gearRecommendations);
            var augmentSummary = RecommendationSummary(augmentRecommendations);
            ship.GearRecommendationOwned = gearSummary.Owned;
            ship.GearRecommendationTotal = gearSummary.Total;
            ship.AugmentRecommendationOwned = augmentSummary.Owned;
            ship.AugmentRecommendationTotal = augmentSummary.Total;
            ship.GearRecommendationGroups = BuildRecommendationGroups(gearRecommendations, 12);
            ship.AugmentRecommendations = BuildFlatRecommendationRows(augmentRecommendations, 90);
        }
    }

    private sealed record RegalShipIndex(
        Dictionary<int, List<JsonElement>> ByGroup,
        Dictionary<int, JsonElement> ById);

    private static RegalShipIndex BuildRegalShipIndex(JsonElement regalShips)
    {
        var rows = regalShips.ValueKind == JsonValueKind.Array
            ? regalShips.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.Object).ToList()
            : [];
        var byId = new Dictionary<int, JsonElement>();
        foreach (var row in rows)
        {
            var id = JsonInt(row, "id");
            // Pairing rows ("Hakuhou (with Shinano or AmagiCV)") share the ship's id; the plain row describes the ship.
            if (id > 0 && (!byId.TryGetValue(id, out var current) ||
                           (IsPairedRegalRow(current) && !IsPairedRegalRow(row))))
            {
                byId[id] = row;
            }
        }

        return new RegalShipIndex(
            rows.GroupBy(row => JsonInt(row, "group"))
                .Where(group => group.Key > 0)
                .ToDictionary(group => group.Key, group => group.ToList()),
            byId);
    }

    private static bool IsPairedRegalRow(JsonElement row) => JsonString(row, "pairType") == "paired";

    private static JsonElement MatchRegalShip(ShipState ship, RegalShipIndex index)
    {
        // Retrofits have their own template ids (101994 for Eldridge (Retrofit)) outside the base group (10126).
        if (index.ById.TryGetValue(ship.TemplateId, out var byId))
        {
            return byId;
        }

        var group = ship.TemplateId / 10;
        if (!index.ByGroup.TryGetValue(group, out var candidates))
        {
            return default;
        }

        var exact = candidates.FirstOrDefault(row => JsonInt(row, "id") == ship.TemplateId);
        if (exact.ValueKind != JsonValueKind.Undefined)
        {
            return exact;
        }

        var baseId = group * 10 + 4;
        var baseShip = candidates.FirstOrDefault(row => JsonInt(row, "id") == baseId);
        return baseShip.ValueKind == JsonValueKind.Undefined ? candidates.FirstOrDefault() : baseShip;
    }

    private static List<RegalRecommendationItem> BuildRegalRecommendations(
        int regalKey,
        RegalGearIndex gearMap,
        IReadOnlyDictionary<string, JsonElement> itemMap,
        Dictionary<string, JsonElement> localAliasMap,
        Dictionary<string, List<OwnedRecommendationItem>> ownedLookup,
        string kind,
        string projectRoot,
        int limit)
    {
        var recommendations = new List<RegalRecommendationItem>();
        if (regalKey <= 0)
        {
            return recommendations;
        }

        var root = gearMap.Ship.GetValueOrDefault(regalKey.ToString(CultureInfo.InvariantCulture));
        if (root.ValueKind != JsonValueKind.Object)
        {
            return recommendations;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var usecaseProperty in root.EnumerateObject())
        {
            var usecase = usecaseProperty.Name;
            var slotGroupId = IntValue(usecaseProperty.Value);
            var slotIndex = 0;
            foreach (var slotIdElement in ArrayItems(
                         gearMap.Usecase.GetValueOrDefault(slotGroupId.ToString(CultureInfo.InvariantCulture))))
            {
                slotIndex++;
                var slotId = IntValue(slotIdElement);
                var rowRank = 0;
                foreach (var rowIdElement in ArrayItems(gearMap.Slot.GetValueOrDefault(slotId.ToString(CultureInfo.InvariantCulture))))
                {
                    rowRank++;
                    var rowId = IntValue(rowIdElement);
                    var equipRank = 0;
                    foreach (var equipRefElement in ArrayItems(
                                 gearMap.Row.GetValueOrDefault(rowId.ToString(CultureInfo.InvariantCulture))))
                    {
                        equipRank++;
                        var record = gearMap.Equip.GetValueOrDefault(ValueText(equipRefElement));
                        if (record.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var itemId = JsonInt(record, "id");
                        if (itemId <= 0 || !seen.Add($"{usecase}|{slotIndex}|{itemId}"))
                        {
                            continue;
                        }

                        recommendations.Add(BuildRegalRecommendationItem(
                            itemId,
                            record,
                            itemMap,
                            localAliasMap,
                            ownedLookup,
                            kind,
                            usecase,
                            slotIndex,
                            rowRank,
                            equipRank,
                            projectRoot));
                        if (recommendations.Count >= limit)
                        {
                            recommendations.Sort(CompareOwnedRecommendationRank);
                            return recommendations;
                        }
                    }
                }
            }
        }

        recommendations.Sort(CompareOwnedRecommendationRank);
        return recommendations;
    }

    private static RegalRecommendationItem BuildRegalRecommendationItem(
        int itemId,
        JsonElement record,
        IReadOnlyDictionary<string, JsonElement> itemMap,
        Dictionary<string, JsonElement> localAliasMap,
        Dictionary<string, List<OwnedRecommendationItem>> ownedLookup,
        string kind,
        string usecase,
        int slot,
        int rowRank,
        int equipRank,
        string projectRoot)
    {
        var key = itemId.ToString(CultureInfo.InvariantCulture);
        var regalInfo = itemMap.GetValueOrDefault(key);
        localAliasMap.TryGetValue(key, out var localInfo);
        ownedLookup.TryGetValue(key, out var owned);
        owned ??= new List<OwnedRecommendationItem>();
        var bestOwned = owned.FirstOrDefault();
        var rarityValue = FirstPositive(JsonInt(regalInfo, "rarity"), JsonInt(localInfo, "rarity"));
        var rarity = kind == "augment"
            ? SpWeaponRarityTier(rarityValue, JsonInt(localInfo, "unique") > 0)
            : RarityTier(rarityValue);

        return new RegalRecommendationItem
        {
            Kind = kind,
            Usecase = usecase,
            Slot = slot,
            RowRank = rowRank,
            EquipRank = equipRank,
            Id = itemId,
            Name = FirstNonEmpty(JsonString(record, "name"), JsonString(localInfo, "name"),
                JsonString(regalInfo, "name"),
                $"{kind}:{itemId}"),
            LocalName = JsonString(localInfo, "name"),
            Condition = JsonString(record, "cond"),
            Note = JsonString(regalInfo, "note"),
            Rarity = rarity,
            RarityClass = string.IsNullOrWhiteSpace(rarity) ? "rarity-unknown" : $"rarity-{rarity}",
            IconUrl = Asset(JsonString(localInfo, "icon_url"), projectRoot),
            Owned = owned.Count > 0,
            OwnedCount = kind == "gear" ? owned.Sum(item => Math.Max(item.Count, 1)) : owned.Count,
            OwnedEnhanceLevel = bestOwned?.EnhanceLevel ?? 0
        };
    }

    private static (int Owned, int Total) RecommendationSummary(List<RegalRecommendationItem> rows)
    {
        var unique = rows.Select(row => row.Id).Where(id => id > 0).ToHashSet();
        var owned = rows.Where(row => row.Owned).Select(row => row.Id).Where(id => id > 0).ToHashSet();
        return (owned.Count, unique.Count);
    }

    private static Dictionary<string, JsonElement> IndexObject(JsonElement map)
    {
        var index = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (map.ValueKind != JsonValueKind.Object)
        {
            return index;
        }

        foreach (var property in map.EnumerateObject())
        {
            index[property.Name] = property.Value;
        }

        return index;
    }

    /// <summary>The sections of regal_gear_map.json (ship → usecase → slot → row → equip), keyed for O(1) lookups.</summary>
    private sealed class RegalGearIndex(JsonElement gearMap)
    {
        public Dictionary<string, JsonElement> Ship { get; } = IndexObject(Child(gearMap, "ship"));
        public Dictionary<string, JsonElement> Usecase { get; } = IndexObject(Child(gearMap, "usecase"));
        public Dictionary<string, JsonElement> Slot { get; } = IndexObject(Child(gearMap, "slot"));
        public Dictionary<string, JsonElement> Row { get; } = IndexObject(Child(gearMap, "row"));
        public Dictionary<string, JsonElement> Equip { get; } = IndexObject(Child(gearMap, "equip"));
    }
}
