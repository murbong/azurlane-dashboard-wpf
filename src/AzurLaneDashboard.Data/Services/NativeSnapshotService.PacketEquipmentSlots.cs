using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static List<EquipmentItemState> BuildEquippedItems(
        List<EquippedItemState> rawItems,
        JsonElement equipMap,
        JsonElement skinMap,
        string projectRoot,
        IReadOnlyDictionary<int, IReadOnlyList<EquipmentAttributeRow>> equipmentAttributes)
    {
        var items = new List<EquipmentItemState>();
        for (var slot = 1; slot <= 5; slot++)
        {
            var raw = rawItems.Count >= slot ? rawItems[slot - 1] : new EquippedItemState(0, 0);
            var templateId = raw.TemplateId;
            var skinId = raw.SkinId;
            var skinInfo = MapObject(skinMap, skinId);
            var skinName = FirstNonEmpty(JsonString(skinInfo, "name"), skinId > 0 ? $"skin:{skinId}" : "");
            var skinIconUrl = Asset(JsonString(skinInfo, "icon_url"), projectRoot);
            if (templateId <= 0)
            {
                items.Add(new EquipmentItemState
                {
                    Slot = slot, Empty = true, TemplateId = 0, SkinId = skinId,
                    IconUrl = skinIconUrl,
                    SkinIconUrl = skinIconUrl,
                    SkinName = skinName
                });
                continue;
            }

            var info = MapObject(equipMap, templateId);
            var rarity = RarityTier(JsonInt(info, "rarity"));
            var attributes = EquipmentAttributes(info, templateId, equipmentAttributes);
            items.Add(new EquipmentItemState
            {
                Slot = slot,
                TemplateId = templateId,
                Empty = false,
                Name = FirstNonEmpty(JsonString(info, "name"), $"equip:{templateId}"),
                IconUrl = Asset(JsonString(info, "icon_url"), projectRoot),
                Rarity = rarity,
                RarityClass = $"rarity-{rarity}",
                EnhanceLevel = EquipmentEnhanceLevel(JsonInt(info, "enhance_level")),
                TypeGroup = EquipmentTypeGroup(JsonInt(info, "type")),
                Attributes = attributes,
                ReloadBonus = EquipmentReloadBonus(attributes),
                SkinId = skinId,
                SkinIconUrl = skinIconUrl,
                SkinName = skinName
            });
        }

        return items;
    }

    private static Dictionary<string, SpecialEquipmentRow> BuildEquippedSpecialLookup(List<PacketRecord> packets,
        JsonElement spweaponMap, string projectRoot)
    {
        var byShip = new Dictionary<string, SpecialEquipmentRow>(StringComparer.Ordinal);
        var relevantRows = RelevantShipStateRowIds(packets);
        foreach (var packet in packets)
        {
            if (relevantRows.Count == 0 || relevantRows.Contains(packet.RowId))
            {
                foreach (var property in new[] { "shiplist", "ship_list" })
                {
                    foreach (var ship in Array(packet.Json, property))
                    {
                        var uid = JsonString(ship, "id");
                        var spweapon = Child(ship, "spweapon");
                        if (!string.IsNullOrWhiteSpace(uid) && spweapon.ValueKind == JsonValueKind.Object)
                        {
                            var row = SpecialEquipmentFromJson(spweapon, spweaponMap, projectRoot);
                            if (row is not null)
                            {
                                byShip[uid] = row;
                            }
                        }
                    }
                }
            }

            if (packet.PacketId is 14001 or 14200)
            {
                var spweapons = Array(packet.Json, "spweapon_list").ToList();
                var shipIds = Array(packet.Json, "ship_id_list").ToList();
                if (spweapons.Count == shipIds.Count)
                {
                    for (var i = 0; i < spweapons.Count; i++)
                    {
                        var row = SpecialEquipmentFromJson(spweapons[i], spweaponMap, projectRoot);
                        if (row is not null)
                        {
                            byShip[ValueText(shipIds[i])] = row;
                        }
                    }
                }
            }
        }

        return byShip;
    }
}
