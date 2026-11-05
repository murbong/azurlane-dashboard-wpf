using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    /// <summary>
    /// Bag equipment from the latest equipment list packet (SC 14001), read the same way the current-state
    /// writer reads it into current_equipment_items.
    /// </summary>
    private static List<BagEquipment> LatestBagEquipment(List<PacketRecord> packets)
    {
        var latest = packets.LastOrDefault(packet => packet.PacketId == 14001 && packet.Direction == "SC");
        if (latest is null)
        {
            return [];
        }

        var items = new List<BagEquipment>();
        foreach (var item in Array(latest.Json, "equip_list"))
        {
            var itemId = JsonInt(item, "id");
            var templateId = FirstPositive(JsonInt(item, "template_id"), JsonInt(item, "config_id"),
                JsonInt(item, "equip_id"), itemId);
            var count = Math.Max(1, FirstPositive(JsonInt(item, "count"), JsonInt(item, "num")));
            if (itemId > 1000 && templateId is > 0 and <= 20 && JsonInt(item, "count") <= 0)
            {
                count = templateId;
                templateId = itemId;
            }

            if (templateId > 0)
            {
                items.Add(new BagEquipment(templateId, count,
                    FirstPositive(JsonInt(item, "enhance_level"), JsonInt(item, "lv"))));
            }
        }

        return items;
    }

    private static List<EquipmentRow> BuildEquipment(IReadOnlyList<BagEquipment> bagItems, List<ShipState> ships,
        JsonElement equipMap, string projectRoot)
    {
        var equipmentAttributes = LoadEquipmentAttributes(projectRoot);
        var byTemplate = new Dictionary<int, EquipmentCounter>();

        EquipmentCounter Counter(int templateId, string fallbackName = "")
        {
            if (!byTemplate.TryGetValue(templateId, out var counter))
            {
                var info = MapObject(equipMap, templateId);
                var rarity = RarityTier(JsonInt(info, "rarity"));
                counter = new EquipmentCounter
                {
                    TemplateId = templateId,
                    Name = FirstNonEmpty(JsonString(info, "name"), fallbackName, $"equip:{templateId}"),
                    IconUrl = Asset(JsonString(info, "icon_url"), projectRoot),
                    Rarity = rarity,
                    RarityClass = $"rarity-{rarity}",
                    EquipmentTypeGroup = EquipmentTypeGroup(JsonInt(info, "type")),
                    Attributes = EquipmentAttributes(info, templateId, equipmentAttributes),
                    EnhanceLevel = EquipmentEnhanceLevel(JsonInt(info, "enhance_level"))
                };
                byTemplate[templateId] = counter;
            }

            return counter;
        }

        foreach (var item in bagItems.Where(item => HasMapKey(equipMap, item.TemplateId)))
        {
            var counter = Counter(item.TemplateId);
            counter.BagCount += item.Count;
            counter.Count += item.Count;
            if (item.EnhanceLevel > 0)
            {
                counter.EnhanceLevel = item.EnhanceLevel;
            }
        }

        foreach (var ship in ships)
        {
            foreach (var item in ship.Equipment.Where(item => !item.Empty && item.TemplateId > 0))
            {
                var counter = Counter(item.TemplateId, item.Name);
                if (item.EnhanceLevel > counter.EnhanceLevel)
                {
                    counter.EnhanceLevel = item.EnhanceLevel;
                }

                if (counter.Attributes.Count == 0 && item.Attributes.Count > 0)
                {
                    counter.Attributes = item.Attributes;
                }

                counter.EquippedCount++;
                counter.Count++;
                counter.EquippedBy.Add($"{ship.DisplayName} #{item.Slot}");
                counter.EquippedShips.Add(new EquipmentEquippedShipRow(
                    ship.Uid,
                    ship.DisplayName,
                    ship.IconUrl,
                    ship.RarityClass,
                    item.Slot));
            }
        }

        return byTemplate.Values
            .Select(counter => new EquipmentRow
            {
                TemplateId = counter.TemplateId.ToString(CultureInfo.InvariantCulture),
                Name = counter.Name,
                IconUrl = counter.IconUrl,
                Rarity = counter.Rarity,
                RarityTier = RarityFilterTier(counter.Rarity),
                RarityClass = counter.RarityClass,
                EquipmentTypeGroup = counter.EquipmentTypeGroup,
                Attributes = counter.Attributes,
                EnhanceLevel = counter.EnhanceLevel,
                Count = counter.Count,
                BagCount = counter.BagCount,
                EquippedCount = counter.EquippedCount,
                EquippedBySummary = string.Join(", ", counter.EquippedBy.Take(8)),
                EquippedShips = counter.EquippedShips
                    .OrderBy(ship => ship.Slot)
                    .ThenBy(ship => ship.ShipName, StringComparer.CurrentCulture)
                    .ToList()
            })
            .OrderByDescending(row => RarityScore(row.Rarity))
            .ThenByDescending(row => row.EnhanceLevel)
            .ThenByDescending(row => row.Count)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static List<EquipmentRow> BuildEquipmentRecommendations(List<EquipmentRow> equipment)
    {
        return equipment
            .Where(item => RarityScore(item.Rarity) >= RarityScore("SR") && item.EnhanceLevel < 13)
            .Select(item => new EquipmentRow
            {
                TemplateId = item.TemplateId,
                Name = item.Name,
                IconUrl = item.IconUrl,
                Rarity = item.Rarity,
                RarityTier = item.RarityTier,
                RarityClass = item.RarityClass,
                EquipmentTypeGroup = item.EquipmentTypeGroup,
                Attributes = item.Attributes,
                EnhanceLevel = item.EnhanceLevel,
                Count = item.Count,
                BagCount = item.BagCount,
                EquippedCount = item.EquippedCount,
                Score = RarityScore(item.Rarity) + Math.Min(item.Count, 8) + Math.Max(0, 13 - item.EnhanceLevel),
                Action = item.EnhanceLevel >= 10 ? "+13 후보" : "강화 후보",
                EquippedBySummary = item.EquippedBySummary,
                EquippedShips = item.EquippedShips
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => RarityScore(item.Rarity))
            .ThenBy(item => item.Name, StringComparer.CurrentCulture)
            .Take(160)
            .ToList();
    }

    private static List<SpecialEquipmentRow> BuildSpecialEquipmentInventory(
        List<PacketRecord> packets,
        JsonElement spweaponMap,
        string projectRoot,
        IReadOnlyCollection<ShipState> ships)
    {
        var latest = packets.LastOrDefault(packet => packet.PacketId == 14001);
        if (latest is null)
        {
            return AttachSpecialEquipmentOwners([], ships)
                .OrderByDescending(item => RarityScore(item.Rarity))
                .ThenByDescending(item => item.EnhanceLevel)
                .ThenBy(item => item.Name, StringComparer.CurrentCulture)
                .ToList();
        }

        var rows = Array(latest.Json, "spweapon_list")
            .Select(item => SpecialEquipmentFromJson(item, spweaponMap, projectRoot))
            .Where(item => item is not null)
            .Cast<SpecialEquipmentRow>()
            .ToList();

        return AttachSpecialEquipmentOwners(rows, ships)
            .OrderByDescending(item => RarityScore(item.Rarity))
            .ThenByDescending(item => item.EnhanceLevel)
            .ThenBy(item => item.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static List<EquipmentSkinRow> BuildEquipmentSkinInventory(List<PacketRecord> packets, JsonElement skinMap,
        string projectRoot)
    {
        var latest = packets.LastOrDefault(packet => packet.PacketId == 14101);
        if (latest is null)
        {
            return new List<EquipmentSkinRow>();
        }

        var rows = new List<EquipmentSkinRow>();
        foreach (var item in Array(latest.Json, "equip_skin_list"))
        {
            var id = JsonInt(item, "id");
            if (id <= 0)
            {
                continue;
            }

            var info = MapObject(skinMap, id);
            rows.Add(new EquipmentSkinRow
            {
                Id = id.ToString(CultureInfo.InvariantCulture),
                Name = FirstNonEmpty(JsonString(info, "name"), $"equip_skin:{id}"),
                IconUrl = Asset(JsonString(info, "icon_url"), projectRoot),
                Rarity = RarityTier(JsonInt(info, "rarity")),
                RarityTier = RarityFilterTier(RarityTier(JsonInt(info, "rarity"))),
                RarityClass = $"rarity-{RarityFilterTier(RarityTier(JsonInt(info, "rarity")))}",
                Count = JsonInt(item, "count"),
                ThemeId = JsonString(info, "themeid")
            });
        }

        return rows.OrderByDescending(row => RarityScore(row.Rarity))
            .ThenBy(row => row.Name, StringComparer.CurrentCulture).ToList();
    }

    private static SpecialEquipmentRow? SpecialEquipmentFromJson(JsonElement item, JsonElement spweaponMap,
        string projectRoot)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var templateId = FirstPositive(JsonInt(item, "template_id"), JsonInt(item, "id"));
        if (templateId <= 0)
        {
            return null;
        }

        var info = MapObject(spweaponMap, templateId);
        var rarity = SpWeaponRarityTier(JsonInt(info, "rarity"), JsonInt(info, "unique") > 0);
        return new SpecialEquipmentRow
        {
            Id = JsonString(item, "id"),
            TemplateId = templateId.ToString(CultureInfo.InvariantCulture),
            Name = FirstNonEmpty(JsonString(info, "name"), $"spweapon:{templateId}"),
            IconUrl = Asset(JsonString(info, "icon_url"), projectRoot),
            Rarity = rarity,
            RarityTier = RarityFilterTier(rarity),
            RarityClass = $"rarity-{RarityFilterTier(rarity)}",
            EnhanceLevel = EquipmentEnhanceLevel(JsonInt(info, "enhance_level")),
            Effect = FirstNonEmpty(JsonString(item, "effect"), JsonString(info, "effect_id")),
            Pt = JsonInt(item, "pt")
        };
    }
}
