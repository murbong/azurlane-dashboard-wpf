using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static List<LevelPriorityRow> BuildLevelPriorities(List<ShipState> ships, JsonElement levelMap)
    {
        return ships
            .Where(ship => !ship.IsBulin)
            .GroupBy(LevelPriorityGroupKey, StringComparer.Ordinal)
            .Select(HighestLevelPriorityShip)
            .Where(ship => ship.Level < 125)
            .Select(ship =>
            {
                var score = RarityScore(ship.Rarity);
                if (ship.Level >= 120) score += 28;
                else if (ship.Level >= 115) score += 36;
                else if (ship.Level >= 110) score += 31;
                else if (ship.Level >= 100) score += 26;
                else if (ship.Level >= 90) score += 17;
                else if (ship.Level >= 70) score += 9;
                if (ship.Locked) score += 8;
                else score -= 12;
                score += Math.Min(18, ship.SkillGap * 2);
                if (ship.Intimacy >= 10000) score += 6;
                if (ship.EmptySlots >= 3 && ship.Level >= 90) score -= 6;
                if (ship.FleetTechLevelPending) score += 30;
                if (ship.FleetTechLevelReloadPending) score += 36;
                score += Math.Min(18, ship.CommunityBestScore / 2);
                return new LevelPriorityRow
                {
                    Score = score,
                    ShipUid = ship.Uid,
                    DisplayName = ship.DisplayName,
                    IconUrl = ship.IconUrl,
                    Rarity = ship.Rarity,
                    RarityTier = RarityFilterTier(ship.Rarity),
                    RarityClass = ship.RarityClass,
                    ShipTypeId = ship.ShipTypeId,
                    ShipTypeLabel = ShipTypeLabel(ship.ShipTypeId),
                    ShipTypeGroup = ShipTypeGroup(ship.ShipTypeId),
                    NationId = ship.NationId,
                    NationLabel = NationLabel(ship.NationId),
                    IsProposed = ship.IsProposed,
                    Level = ship.Level,
                    LevelsTo120 = Math.Max(0, 120 - ship.Level),
                    LevelsTo125 = Math.Max(0, 125 - ship.Level),
                    ExpCurrent = ship.ExpCurrent,
                    ExpRequired = ship.ExpRequired,
                    ExpPercent = ship.ExpPercent,
                    ExpIsMax = ship.ExpIsMax,
                    MoodValue = ship.Energy,
                    MoodLabel = MoodLabel(ship.Energy),
                    CreateTime = ship.CreateTime,
                    SkillSummary = ship.SkillSummary,
                    FleetTechLevelBonus = ship.FleetTechLevelBonus,
                    FleetTechLevelPending = ship.FleetTechLevelPending,
                    FleetTechLevelReloadPending = ship.FleetTechLevelReloadPending,
                    CommunityLabel = ship.CommunityLabel,
                    CommunitySummary = ship.CommunitySummary,
                    CommunityScore = ship.CommunityScore,
                    CommunityBestScore = ship.CommunityBestScore,
                    CommunityRatings = ship.CommunityRatings,
                    RunsTo120Avg = ship.RunsTo120Avg,
                    RunsTo125Avg = ship.RunsTo125Avg,
                    RecentBattleExpAvg = ship.RecentBattleExpAvg
                };
            })
            .OrderByDescending(row => row.Score)
            .ThenByDescending(row => row.Level)
            .Take(1000)
            .ToList();
    }

    private static string LevelPriorityGroupKey(ShipState ship)
    {
        var groupId = FirstPositive(ship.GroupId, ship.TemplateId / 10, ship.TemplateId);
        return groupId > 0 ? groupId.ToString(CultureInfo.InvariantCulture) : ship.Uid;
    }

    private static ShipState HighestLevelPriorityShip(IGrouping<string, ShipState> group)
    {
        return group
            .OrderByDescending(ship => ship.Level)
            .ThenByDescending(ship => ship.Exp)
            .ThenByDescending(ship => ship.Locked)
            .ThenByDescending(ship => ship.Intimacy)
            .ThenBy(ship => ship.CreateTime > 0 ? ship.CreateTime : int.MaxValue)
            .First();
    }

    private static List<SkillPriorityRow> BuildSkillPriorities(List<ShipState> ships, JsonElement skillMap,
        string projectRoot)
    {
        var rows = new List<SkillPriorityRow>();
        foreach (var ship in ships.Where(ship => !ship.IsBulin))
        {
            foreach (var skill in ship.Skills)
            {
                var info = MapObject(skillMap, skill.Id);
                var maxLevel = JsonInt(info, "max_level");
                if (maxLevel <= 1 && info.ValueKind == JsonValueKind.Object)
                {
                    continue;
                }

                var target = maxLevel > 1 ? maxLevel : 10;
                if (skill.Level <= 0 || skill.Level >= target)
                {
                    continue;
                }

                rows.Add(new SkillPriorityRow
                {
                    Score = RarityScore(ship.Rarity) + Math.Min(25, ship.Level / 4) + (target - skill.Level) * 2 +
                            (ship.Locked ? 5 : 0),
                    ShipUid = ship.Uid,
                    ShipName = ship.DisplayName,
                    ShipIconUrl = ship.IconUrl,
                    Rarity = ship.Rarity,
                    IsProposed = ship.IsProposed,
                    ShipLevel = ship.Level,
                    SkillId = skill.Id.ToString(CultureInfo.InvariantCulture),
                    SkillName = FirstNonEmpty(JsonString(info, "name"), $"ID {skill.Id}"),
                    SkillIconUrl = Asset(JsonString(info, "icon_url"), projectRoot),
                    SkillLevel = skill.Level,
                    SkillMaxLevel = target,
                    SkillExp = skill.Exp,
                    Reason = $"{ship.Rarity} · {ship.Level}레벨 · {target - skill.Level}레벨 남음"
                });
            }
        }

        return rows.OrderByDescending(row => row.Score).ThenByDescending(row => row.ShipLevel).Take(1200).ToList();
    }

    private static List<GearGapRow> BuildGearGaps(List<ShipState> ships)
    {
        // Most stored ships are stripped on purpose, so sortie-fleet ships lead the list as the actionable gaps.
        return ships
            .Where(ship => !ship.IsBulin && ship.EmptySlots > 0 &&
                           (ship.FleetId > 0 || ship.Locked || ship.Level >= 90 ||
                            RarityScore(ship.Rarity) >= RarityScore("SSR")))
            .OrderBy(ship => ship.FleetId > 0 ? FleetSortOrder(ship.FleetId) : int.MaxValue)
            .ThenByDescending(ship => ship.Level)
            .ThenByDescending(ship => ship.EmptySlots)
            .Select(ship => new GearGapRow
            {
                ShipUid = ship.Uid,
                DisplayName = ship.DisplayName,
                IconUrl = ship.IconUrl,
                Rarity = ship.Rarity,
                RarityTier = RarityFilterTier(ship.Rarity),
                RarityClass = ship.RarityClass,
                ShipTypeId = ship.ShipTypeId,
                ShipTypeLabel = ShipTypeLabel(ship.ShipTypeId),
                ShipTypeGroup = ShipTypeGroup(ship.ShipTypeId),
                NationId = ship.NationId,
                NationLabel = NationLabel(ship.NationId),
                IsProposed = ship.IsProposed,
                Level = ship.Level,
                EmptySlots = ship.EmptySlots,
                FleetLabel = ship.FleetLabel,
                FleetOrder = ship.FleetId > 0 ? FleetSortOrder(ship.FleetId) : 0,
                EquippedSummary = ship.EquippedSummary,
                SpecialEquipmentSummary = ship.SpecialEquipmentSummary,
                EquipmentSlots = ship.Equipment.Select(ToShipEquipmentSlotRow).ToList(),
                SpecialEquipmentSlot = ToSpecialEquipmentSlotRow(ship.SpecialEquipment)
            })
            .Take(800)
            .ToList();
    }

    private static List<CleanupCandidateRow> BuildCleanupCandidates(List<ShipState> ships)
    {
        return ships
            .Where(ship => !ship.Locked && ship.Level <= 1 && !ship.IsBulin)
            .GroupBy(ship => ship.TemplateId)
            .Where(group => group.Count() >= 3)
            .Select(group => new CleanupCandidateRow
            {
                TemplateId = group.Key.ToString(CultureInfo.InvariantCulture),
                Name = group.First().Name,
                Count = group.Count(),
                Reason = $"저레벨 미잠금 {group.Count()}명"
            })
            .OrderByDescending(row => row.Count)
            .Take(120)
            .ToList();
    }

    private static List<CommunityGearShortageRow> BuildCommunityGearShortages(List<ShipState> ships)
    {
        var shortages = new Dictionary<int, GearShortageAccumulator>();
        foreach (var ship in ships.Where(ship => !ship.IsBulin && ship.CommunityBestScore >= 22))
        {
            foreach (var recommendation in ship.GearRecommendationGroups.SelectMany(group => group.Items)
                         .Where(item => !item.Owned))
            {
                var itemId = ToInt(recommendation.Id);
                if (itemId <= 0)
                {
                    continue;
                }

                if (!shortages.TryGetValue(itemId, out var accumulator))
                {
                    accumulator = new GearShortageAccumulator
                    {
                        Id = itemId,
                        Name = FirstNonEmpty(recommendation.LocalName, recommendation.DisplayName, $"equip:{itemId}"),
                        IconUrl = recommendation.IconUrl,
                        Rarity = recommendation.Rarity,
                        RarityScore = RarityScore(recommendation.Rarity)
                    };
                    shortages[itemId] = accumulator;
                }

                if (!accumulator.ShipUids.Add(ship.Uid))
                {
                    continue;
                }

                accumulator.MissingForCount++;
                if (accumulator.TopShipNames.Count < 4)
                {
                    accumulator.TopShipNames.Add(ship.DisplayName);
                }

                accumulator.Score += 4 + Math.Min(18, ship.CommunityBestScore / 3) +
                                     Math.Min(20, RarityScore(recommendation.Rarity) / 4);
            }
        }

        return shortages.Values
            .Select(item => new CommunityGearShortageRow
            {
                Id = item.Id.ToString(CultureInfo.InvariantCulture),
                Name = item.Name,
                IconUrl = item.IconUrl,
                Rarity = item.Rarity,
                RarityTier = RarityFilterTier(item.Rarity),
                RarityClass = $"rarity-{RarityFilterTier(item.Rarity)}",
                MissingForCount = item.MissingForCount,
                TopShipNames = string.Join(", ", item.TopShipNames),
                Score = item.Score + item.MissingForCount * 6
            })
            .OrderByDescending(row => row.Score)
            .ThenByDescending(row => row.MissingForCount)
            .ThenByDescending(row => RarityScore(row.Rarity))
            .ThenBy(row => row.Name, StringComparer.CurrentCulture)
            .Take(120)
            .ToList();
    }

    private static void ApplyBattleExpUpdates(List<ShipState> ships, List<PacketRecord> packets, JsonElement levelMap)
    {
        var byUid = ships.ToDictionary(ship => ship.Uid, StringComparer.Ordinal);
        foreach (var packet in packets.Where(packet => packet.PacketId == 40004 && packet.Direction == "SC"))
        {
            foreach (var exp in Array(packet.Json, "ship_exp_list"))
            {
                var uid = JsonString(exp, "ship_id");
                if (!byUid.TryGetValue(uid, out var ship) || packet.RowId <= ship.SnapshotRowId)
                {
                    continue;
                }

                var gained = JsonInt(exp, "exp");
                if (gained <= 0)
                {
                    continue;
                }

                var next = AddShipExp(ship.Level, ship.Exp, gained, ship.Rarity, levelMap);
                ship.Level = next.Level;
                ship.Exp = next.Exp;
                var progress = LevelProgress(ship.Level, ship.Exp, ship.Rarity, levelMap);
                ship.ExpCurrent = progress.Current;
                ship.ExpRequired = progress.Required;
                ship.ExpPercent = progress.Percent;
                ship.ExpIsMax = progress.IsMax;
            }
        }
    }

    private static void ApplyFleetTechUnlockState(List<ShipState> ships)
    {
        foreach (var ship in ships)
        {
            ship.FleetTechLevelPending = !ship.IsBulin
                                         && ship.Level < FleetTechLevelThreshold
                                         && !string.IsNullOrWhiteSpace(ship.FleetTechLevelBonus);
            ship.FleetTechLevelReloadPending = ship.FleetTechLevelPending &&
                                                ship.FleetTechLevelBonus.Contains("장전",
                                                    StringComparison.Ordinal);
        }
    }
}
