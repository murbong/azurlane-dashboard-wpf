using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static ShipRow ToShipRow(ShipState ship, JsonElement skillMap)
    {
        return new ShipRow
        {
            ShipUid = ship.Uid,
            TemplateId = ship.TemplateId.ToString(CultureInfo.InvariantCulture),
            Name = ship.Name,
            CustomName = ship.CustomName,
            IconUrl = ship.IconUrl,
            BackgroundUrl = ship.BackgroundUrl,
            Rarity = ship.Rarity,
            RarityTier = RarityFilterTier(ship.Rarity),
            RarityClass = ship.RarityClass,
            Level = ship.Level,
            LimitBreak = ship.LimitBreak,
            EnhanceLevel = ship.EnhanceLevel,
            EnhanceText = ship.EnhanceText,
            Exp = ship.Exp,
            ExpCurrent = ship.ExpCurrent,
            ExpRequired = ship.ExpRequired,
            ExpPercent = ship.ExpPercent,
            ExpIsMax = ship.ExpIsMax,
            Intimacy = ship.Intimacy,
            MoodValue = ship.Energy,
            MoodLabel = MoodLabel(ship.Energy),
            CreateTime = ship.CreateTime,
            StatsSummary = ship.StatsSummary,
            StatsDetail = ship.StatsDetail,
            StatRows = ship.StatRows,
            Reload = ship.Reload,
            ShipTypeId = ship.ShipTypeId,
            ShipTypeLabel = ShipTypeLabel(ship.ShipTypeId),
            ShipTypeGroup = ShipTypeGroup(ship.ShipTypeId),
            NationId = ship.NationId,
            NationLabel = NationLabel(ship.NationId),
            SkinId = ship.SkinId,
            SkinName = ship.SkinName,
            Locked = ship.Locked,
            IsProposed = ship.IsProposed,
            EquippedSummary = ship.EquippedSummary,
            SpecialEquipmentSummary = ship.SpecialEquipmentSummary,
            SkillSummary = ship.SkillSummary,
            SkillGap = ship.SkillGap,
            EmptySlots = ship.EmptySlots,
            FleetLabel = ship.FleetLabel,
            FleetTechGetBonus = ship.FleetTechGetBonus,
            FleetTechLevelBonus = ship.FleetTechLevelBonus,
            FleetTechLevelPending = ship.FleetTechLevelPending,
            FleetTechLevelReloadPending = ship.FleetTechLevelReloadPending,
            CommunityLabel = ship.CommunityLabel,
            CommunitySummary = ship.CommunitySummary,
            CommunityScore = ship.CommunityScore,
            CommunityBestScore = ship.CommunityBestScore,
            RunsTo120Avg = ship.RunsTo120Avg,
            RunsTo125Avg = ship.RunsTo125Avg,
            RecentBattleExpAvg = ship.RecentBattleExpAvg,
            HasFullState = ship.HasFullState,
            IsBulin = ship.IsBulin,
            EquipmentSlots = ship.Equipment.Select(ToShipEquipmentSlotRow).ToList(),
            SpecialEquipmentSlot = ToSpecialEquipmentSlotRow(ship.SpecialEquipment),
            SkillDetails = ship.Skills.Select((skill, index) => new ShipSkillDetailRow
            {
                Index = index + 1,
                SkillId = skill.Id.ToString(CultureInfo.InvariantCulture),
                Name = JsonString(MapObject(skillMap, skill.Id), "name"),
                MaxLevel = JsonInt(MapObject(skillMap, skill.Id), "max_level") is > 0 and var maxLevel
                    ? maxLevel : null,
                Level = skill.Level,
                Exp = skill.Exp
            }).ToList(),
            CommunityRatings = ship.CommunityRatings,
            GearRecommendationGroups = ship.GearRecommendationGroups,
            AugmentRecommendations = ship.AugmentRecommendations,
            GearRecommendationOwned = ship.GearRecommendationOwned,
            GearRecommendationTotal = ship.GearRecommendationTotal,
            AugmentRecommendationOwned = ship.AugmentRecommendationOwned,
            AugmentRecommendationTotal = ship.AugmentRecommendationTotal
        };
    }

    private static ShipEquipmentSlotRow ToShipEquipmentSlotRow(EquipmentItemState item)
    {
        return new ShipEquipmentSlotRow
        {
            Slot = item.Slot,
            Empty = item.Empty,
            TemplateId = item.TemplateId > 0 ? item.TemplateId.ToString(CultureInfo.InvariantCulture) : "",
            Name = item.Name,
            IconUrl = item.IconUrl,
            SkinIconUrl = item.SkinIconUrl,
            Rarity = item.Rarity,
            RarityClass = item.RarityClass,
            EnhanceLevel = item.EnhanceLevel,
            TypeGroup = item.TypeGroup,
            Attributes = item.Attributes,
            SkinName = item.SkinName
        };
    }

    private static ShipEquipmentSlotRow? ToSpecialEquipmentSlotRow(SpecialEquipmentRow? item)
    {
        if (item is null)
        {
            return null;
        }

        return new ShipEquipmentSlotRow
        {
            Slot = 0,
            Empty = false,
            TemplateId = item.TemplateId,
            Name = item.Name,
            IconUrl = item.IconUrl,
            Rarity = item.Rarity,
            RarityClass = $"rarity-{item.Rarity}",
            EnhanceLevel = item.EnhanceLevel,
            TypeGroup = "특수장비",
            SkinName = item.Effect
        };
    }
}
