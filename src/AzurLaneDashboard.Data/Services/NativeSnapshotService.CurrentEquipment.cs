using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static List<EquipmentRow> BuildCurrentEquipmentInventory(SqliteConnection connection, JsonElement equipMap,
        string projectRoot, List<ShipState> ships)
    {
        var equipmentAttributes = LoadEquipmentAttributes(projectRoot);
        var byTemplate = new Dictionary<int, EquipmentCounter>();
        if (TableExists(connection, "current_equipment_items"))
        {
            // Bag items only; equipped pieces are counted from the ship slots below.
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT equipment_template_id,
                                         MAX(enhance_level) AS enhance_level,
                                         SUM(CASE WHEN count > 0 THEN count ELSE 1 END) AS bag_count
                                  FROM current_equipment_items
                                  WHERE equipment_template_id > 0
                                  GROUP BY equipment_template_id
                                  """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var templateId = Int(reader, "equipment_template_id");
                if (!HasMapKey(equipMap, templateId))
                {
                    continue;
                }

                var info = MapObject(equipMap, templateId);
                var rarity = RarityTier(JsonInt(info, "rarity"));
                var bagCount = Int(reader, "bag_count");
                byTemplate[templateId] = new EquipmentCounter
                {
                    TemplateId = templateId,
                    Name = FirstNonEmpty(JsonString(info, "name"), $"equip:{templateId}"),
                    IconUrl = Asset(JsonString(info, "icon_url"), projectRoot),
                    Rarity = rarity,
                    RarityClass = $"rarity-{RarityFilterTier(rarity)}",
                    EquipmentTypeGroup = EquipmentTypeGroup(JsonInt(info, "type")),
                    Attributes = EquipmentAttributes(info, templateId, equipmentAttributes),
                    EnhanceLevel = FirstPositive(Int(reader, "enhance_level"),
                        EquipmentEnhanceLevel(JsonInt(info, "enhance_level"))),
                    Count = bagCount,
                    BagCount = bagCount
                };
            }
        }

        var equippedShipSlots = ships
            .SelectMany(ship => ship.Equipment
                .Where(item => !item.Empty && item.TemplateId > 0)
                .Select(item => (ship, item)))
            .ToList();
        if (equippedShipSlots.Count > 0)
        {
            foreach (var counter in byTemplate.Values)
            {
                counter.Count = counter.BagCount;
                counter.EquippedCount = 0;
                counter.EquippedBy.Clear();
                counter.EquippedShips.Clear();
            }

            foreach (var (ship, item) in equippedShipSlots)
            {
                if (!byTemplate.TryGetValue(item.TemplateId, out var counter))
                {
                    counter = new EquipmentCounter
                    {
                        TemplateId = item.TemplateId,
                        Name = item.Name,
                        IconUrl = item.IconUrl,
                        Rarity = item.Rarity,
                        RarityClass = item.RarityClass,
                        EquipmentTypeGroup = item.TypeGroup,
                        Attributes = item.Attributes,
                        EnhanceLevel = item.EnhanceLevel
                    };
                    byTemplate[item.TemplateId] = counter;
                }

                if (counter.Attributes.Count == 0 && item.Attributes.Count > 0)
                {
                    counter.Attributes = item.Attributes;
                }

                if (item.EnhanceLevel > counter.EnhanceLevel)
                {
                    counter.EnhanceLevel = item.EnhanceLevel;
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

    private static List<SpecialEquipmentRow> BuildCurrentSpecialEquipmentInventory(SqliteConnection connection,
        JsonElement spweaponMap, string projectRoot, IReadOnlyCollection<ShipState> ships)
    {
        var rows = new List<SpecialEquipmentRow>();
        if (!TableExists(connection, "current_spweapon_inventory"))
        {
            return AttachSpecialEquipmentOwners(rows, ships)
                .OrderByDescending(row => RarityScore(row.Rarity))
                .ThenByDescending(row => row.EnhanceLevel)
                .ThenBy(row => row.Name, StringComparer.CurrentCulture)
                .ToList();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM current_spweapon_inventory";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(CurrentSpecialEquipmentRow(reader, spweaponMap, projectRoot));
        }

        return AttachSpecialEquipmentOwners(rows, ships)
            .OrderByDescending(row => RarityScore(row.Rarity))
            .ThenByDescending(row => row.EnhanceLevel)
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static SpecialEquipmentRow CurrentSpecialEquipmentRow(SqliteDataReader reader, JsonElement spweaponMap,
        string projectRoot)
    {
        var templateId = Int(reader, "template_id");
        var info = MapObject(spweaponMap, templateId);
        var rarity = SpWeaponRarityTier(JsonInt(info, "rarity"), JsonInt(info, "unique") > 0);
        return new SpecialEquipmentRow
        {
            Id = Text(reader, "spweapon_uid"),
            TemplateId = templateId.ToString(CultureInfo.InvariantCulture),
            Name = FirstNonEmpty(JsonString(info, "name"), $"spweapon:{templateId}"),
            IconUrl = Asset(JsonString(info, "icon_url"), projectRoot),
            Rarity = rarity,
            RarityTier = RarityFilterTier(rarity),
            RarityClass = $"rarity-{RarityFilterTier(rarity)}",
            EnhanceLevel = FirstPositive(Int(reader, "enhance_level"), EquipmentEnhanceLevel(JsonInt(info, "enhance_level"))),
            Effect = Text(reader, "effect"),
            Pt = Int(reader, "pt")
        };
    }

    private static List<SpecialEquipmentRow> AttachSpecialEquipmentOwners(
        IEnumerable<SpecialEquipmentRow> rows,
        IReadOnlyCollection<ShipState> ships)
    {
        var inventoryRows = rows.ToList();
        var knownIds = inventoryRows
            .Select(row => row.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var special in ships
                     .Select(ship => ship.SpecialEquipment)
                     .Where(special => special is not null)
                     .Cast<SpecialEquipmentRow>())
        {
            if (!string.IsNullOrWhiteSpace(special.Id) && knownIds.Add(special.Id))
            {
                inventoryRows.Add(special);
            }
        }

        var ownersBySpecialId = ships
            .Where(ship => ship.SpecialEquipment is not null &&
                           !string.IsNullOrWhiteSpace(ship.SpecialEquipment.Id))
            .GroupBy(ship => ship.SpecialEquipment!.Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(ship => ship.DisplayName, StringComparer.CurrentCulture)
                    .Select(ship => new EquipmentEquippedShipRow(
                        ship.Uid,
                        ship.DisplayName,
                        ship.IconUrl,
                        ship.RarityClass,
                        0))
                    .ToArray(),
                StringComparer.Ordinal);

        return inventoryRows
            .Select(row => WithSpecialEquipmentOwners(
                row,
                !string.IsNullOrWhiteSpace(row.Id) && ownersBySpecialId.TryGetValue(row.Id, out var owners)
                    ? owners
                    : []))
            .ToList();
    }

    private static SpecialEquipmentRow WithSpecialEquipmentOwners(
        SpecialEquipmentRow row,
        IReadOnlyList<EquipmentEquippedShipRow> owners)
    {
        return new SpecialEquipmentRow
        {
            Id = row.Id,
            TemplateId = row.TemplateId,
            Name = row.Name,
            IconUrl = row.IconUrl,
            Rarity = row.Rarity,
            RarityTier = row.RarityTier,
            RarityClass = row.RarityClass,
            EnhanceLevel = row.EnhanceLevel,
            Effect = row.Effect,
            Pt = row.Pt,
            EquippedBySummary = string.Join(", ", owners.Select(owner => owner.ShipName)),
            EquippedShips = owners
        };
    }

    private static List<EquipmentSkinRow> BuildCurrentEquipmentSkins(SqliteConnection connection, JsonElement skinMap,
        string projectRoot)
    {
        var rows = new List<EquipmentSkinRow>();
        if (!TableExists(connection, "current_equipment_skins"))
        {
            return rows;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM current_equipment_skins";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = Int(reader, "skin_id");
            var info = MapObject(skinMap, id);
            var rarity = RarityTier(JsonInt(info, "rarity"));
            rows.Add(new EquipmentSkinRow
            {
                Id = id.ToString(CultureInfo.InvariantCulture),
                Name = FirstNonEmpty(JsonString(info, "name"), $"equip_skin:{id}"),
                IconUrl = Asset(JsonString(info, "icon_url"), projectRoot),
                Rarity = rarity,
                RarityTier = RarityFilterTier(rarity),
                RarityClass = $"rarity-{RarityFilterTier(rarity)}",
                Count = Int(reader, "count"),
                ThemeId = JsonString(info, "themeid")
            });
        }

        return rows.OrderByDescending(row => RarityScore(row.Rarity))
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .ToList();
    }
}
