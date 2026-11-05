using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static List<ShipState> BuildShips(
        Dictionary<string, FullShipState> fullShips,
        Dictionary<string, SpecialEquipmentRow> specialLookup,
        SnapshotMaps maps,
        string projectRoot)
    {
        var ships = new List<ShipState>();
        if (fullShips.Count == 0)
        {
            return ships;
        }

        var shipStats = LoadShipStats(projectRoot);
        var equipmentAttributes = LoadEquipmentAttributes(projectRoot);
        foreach (var (uid, full) in fullShips)
        {
            var templateId = full.TemplateId;
            var shipInfo = MapObject(maps.Ships, templateId);
            var skinId = FirstPositive(full.SkinId, JsonInt(shipInfo, "skin_id"));
            var skinInfo = MapObject(maps.Skins, skinId);
            var baseRarity = JsonInt(shipInfo, "rarity");
            var groupId = FirstPositive(JsonInt(shipInfo, "group_type"), templateId / 10);
            var isRetrofitted = RetrofitComplete(full.TransformIds, groupId, maps.ShipTransforms);
            var rarity = ShipDisplayRarity(baseRarity, templateId, groupId, maps.ShipBlueprints, isRetrofitted);
            var level = full.Level;
            var exp = full.Exp;
            var progress = LevelProgress(level, exp, rarity, maps.ShipLevels);
            var equipment = BuildEquippedItems(full.Equipment, maps.Equipment,
                maps.EquipmentSkins, projectRoot, equipmentAttributes);
            var special = specialLookup.GetValueOrDefault(uid);
            var skills = full.Skills;
            var fleetTech = ResolveFleetTech(templateId, shipInfo, skinId, maps.FleetTech);
            shipStats.TryGetValue(templateId, out var stats);
            var effectiveStats = EffectiveStats(
                stats,
                level,
                full.Intimacy,
                full.Strength,
                full.TransformLevels,
                equipment);
            var enhancement = ShipEnhancement(stats, level, full.Strength, true);

            var ship = new ShipState
            {
                Uid = uid,
                TemplateId = templateId,
                GroupId = groupId,
                Name = FirstNonEmpty(JsonString(shipInfo, "name"), uid),
                CustomName = full.CustomName,
                IconUrl = Asset(FirstNonEmpty(JsonString(skinInfo, "icon_url"),
                    JsonString(skinInfo, "shipyard_url"), JsonString(skinInfo, "qicon_url")), projectRoot),
                BackgroundUrl =
                    Asset(
                        FirstNonEmpty(JsonString(skinInfo, "shipyard_url"),
                            JsonString(skinInfo, "icon_url")), projectRoot),
                Rarity = rarity,
                RarityClass = RarityCssClass(rarity),
                RarityScore = RarityScore(rarity),
                Level = level,
                LimitBreak = JsonInt(shipInfo, "limit_break"),
                EnhanceLevel = enhancement.Value,
                EnhanceText = enhancement.Text,
                Exp = exp,
                ExpCurrent = progress.Current,
                ExpRequired = progress.Required,
                ExpPercent = progress.Percent,
                ExpIsMax = progress.IsMax,
                Energy = full.Energy,
                Intimacy = full.Intimacy,
                CreateTime = full.CreateTime,
                StatsSummary = ShipStatsSummary(effectiveStats),
                StatsDetail = ShipStatsDetail(effectiveStats),
                StatRows = ShipStatRows(effectiveStats),
                Reload = effectiveStats?.Reload ?? 0,
                ShipTypeId = JsonInt(shipInfo, "ship_type"),
                NationId = JsonInt(shipInfo, "nationality"),
                DefaultEquipmentIds = stats?.DefaultEquipmentIds ?? [],
                DefaultEquipmentTypes = stats?.DefaultEquipmentTypes ?? [],
                AirSlotWeights = stats?.AirSlotWeights ?? [],
                SkinId = skinId,
                SkinName = JsonString(skinInfo, "name"),
                Locked = full.Locked,
                Skills = skills,
                SkillSummary = skills.Count == 0 ? "-" : string.Join("/", skills.Select(skill => skill.Level)),
                SkillGap = SkillGap(skills, maps.Skills),
                Equipment = equipment,
                EquippedSummary = EquippedSummary(equipment),
                EmptySlots = equipment.Count(item => item.Empty),
                SpecialEquipment = special,
                SpecialEquipmentSummary = special is null ? "" :
                    special.EnhanceLevel > 0 ? $"{special.Name} +{special.EnhanceLevel}" : special.Name,
                FleetTechGetBonus = FleetBonusText(fleetTech, "get"),
                FleetTechLevelBonus = FleetBonusText(fleetTech, "level"),
                SnapshotRowId = full.SourceRowId,
                HasFullState = full.HasActiveState,
                IsBulin = IsBulin(templateId, JsonString(shipInfo, "name"))
            };
            ships.Add(ship);
        }

        ships.Sort((a, b) => CompareTuple(b.Level, b.RarityScore, b.Name, a.Level, a.RarityScore, a.Name));
        return ships;
    }

    private static Dictionary<string, FullShipState> CollectFullShips(List<PacketRecord> packets)
    {
        var ships = new Dictionary<string, FullShipState>(StringComparer.Ordinal);
        FullEquipmentChange? pendingEquip = null;
        FullEquipmentSkinChange? pendingSkin = null;
        var relevantRows = RelevantShipStateRowIds(packets);

        foreach (var packet in packets)
        {
            if (relevantRows.Count > 0 && !relevantRows.Contains(packet.RowId))
            {
                continue;
            }

            foreach (var property in new[] { "shiplist", "ship_list" })
            {
                foreach (var raw in Array(packet.Json, property))
                {
                    var uid = JsonInt(raw, "id").ToString(CultureInfo.InvariantCulture);
                    if (uid == "0")
                    {
                        continue;
                    }

                    ships[uid] = FullShipState.FromJson(raw, packet.RowId);
                }
            }

            if (packet.PacketId == 12006 &&
                (packet.Direction == "CS" || HasProperties(packet.Json, "ship_id", "equip_id", "pos")))
            {
                pendingEquip = new FullEquipmentChange(JsonString(packet.Json, "ship_id"), JsonInt(packet.Json, "pos"),
                    JsonInt(packet.Json, "equip_id"));
            }
            else if (packet.PacketId == 12007 && JsonInt(packet.Json, "result") == 0 && pendingEquip is not null)
            {
                if (ships.TryGetValue(pendingEquip.ShipUid, out var ship))
                {
                    ship.SetEquipment(pendingEquip.Position, pendingEquip.EquipmentId, 0);
                }

                pendingEquip = null;
            }
            else if (packet.PacketId == 12036 &&
                     (packet.Direction == "CS" || HasProperties(packet.Json, "ship_id", "equip_skin_id", "pos")))
            {
                pendingSkin = new FullEquipmentSkinChange(JsonString(packet.Json, "ship_id"),
                    JsonInt(packet.Json, "pos"), JsonInt(packet.Json, "equip_skin_id"));
            }
            else if (packet.PacketId == 12037 && JsonInt(packet.Json, "result") == 0 && pendingSkin is not null)
            {
                if (ships.TryGetValue(pendingSkin.ShipUid, out var ship))
                {
                    ship.SetEquipmentSkin(pendingSkin.Position, pendingSkin.SkinId);
                }

                pendingSkin = null;
            }
        }

        return ships;
    }

    private static HashSet<int> RelevantShipStateRowIds(List<PacketRecord> packets)
    {
        var rows = new HashSet<int>();
        var latestShipListRowId = packets
            .Where(packet => packet.PacketId == 12001 && packet.Direction == "SC")
            .Select(packet => packet.RowId)
            .DefaultIfEmpty(0)
            .Max();
        if (latestShipListRowId > 0)
        {
            foreach (var packet in packets.Where(packet =>
                         packet.RowId >= latestShipListRowId
                         && (ShipListPacketIds.Contains(packet.PacketId) ||
                             SpWeaponStatePacketIds.Contains(packet.PacketId))))
            {
                rows.Add(packet.RowId);
            }
        }

        foreach (var packetId in LatestStatePacketIds)
        {
            var latest = packets
                .Where(packet => packet.PacketId == packetId && packet.Direction == "SC")
                .OrderByDescending(packet => packet.RowId)
                .FirstOrDefault();
            if (latest is not null)
            {
                rows.Add(latest.RowId);
            }
        }

        var latestSystemBaseRowIds = packets
            .Where(packet => SystemBasePacketIds.Contains(packet.PacketId) && packet.Direction == "SC")
            .Select(packet => packet.RowId)
            .ToList();
        if (latestSystemBaseRowIds.Count > 0)
        {
            var startRowId = latestSystemBaseRowIds.Min();
            foreach (var packet in packets.Where(packet =>
                         packet.RowId >= startRowId && SystemReconstructionPacketIds.Contains(packet.PacketId)))
            {
                rows.Add(packet.RowId);
            }
        }

        return rows;
    }
}
