using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool MatchesShipRow(object row)
    {
        var text = appliedQuery;
        return (text.Length == 0 || ShipSearchText(row).Contains(text))
               && MatchesRarity(ShipRarityTier(row))
               && MatchesShipType(row)
               && MatchesNation(row)
               && MatchesShipExtra(row);
    }

    private bool MatchesShipType(object row)
    {
        return SelectedShipTypeFilter switch
        {
            null or "" or "전체" => true,
            "전열" => ShipTypeGroup(row) == "vanguard",
            "주력" => ShipTypeGroup(row) == "main",
            "잠수" => ShipTypeGroup(row) == "submarine",
            "구축" => ShipTypeId(row) is 1 or 20 or 21,
            "경순" => ShipTypeId(row) == 2,
            "중순" => ShipTypeId(row) is 3 or 18,
            "전함" => ShipTypeId(row) is 4 or 5 or 10 or 13,
            "항모" => ShipTypeId(row) is 6 or 7,
            "기타" => ShipTypeGroup(row) is not ("vanguard" or "main" or "submarine"),
            _ => ShipTypeLabel(row) == SelectedShipTypeFilter
        };
    }

    private bool MatchesNation(object row)
    {
        return string.IsNullOrWhiteSpace(SelectedNationFilter)
               || SelectedNationFilter == "전체"
               || NationLabel(row) == SelectedNationFilter;
    }

    private bool MatchesShipExtra(object row)
    {
        return SelectedShipExtraFilter switch
        {
            "커뮤니티 A+" => CommunityBestScore(row) >= 34,
            "120 기술" => FleetTechLevelPending(row),
            "스킬작" => SkillGap(row) > 0,
            "장비 빈칸" => EmptySlots(row) > 0,
            "잠금" => Locked(row),
            "미잠금" => !Locked(row),
            "레벨 부족" => ShipLevel(row) < 120,
            "추천 부족" => GearRecommendationTotal(row) > GearRecommendationOwned(row) ||
                       AugmentRecommendationTotal(row) > AugmentRecommendationOwned(row),
            _ => true
        };
    }

    private ShipRow? SourceShip(object? row)
    {
        return row switch
        {
            null => null,
            string shipUid => ShipByUid(shipUid),
            ShipRow ship => ship,
            LevelPriorityRow level => ShipByUid(level.ShipUid),
            BattleShipRateRow rate => ShipByUid(rate.ShipUid),
            GearGapRow gap => ShipByUid(gap.ShipUid),
            FleetDamageRow damage => ShipByUid(damage.ShipUid),
            FleetCompositionShipRow compositionShip => ShipByUid(compositionShip.ShipUid),
            FleetCompositionSlotRow slot => SourceShip(slot.Ship),
            SkillPriorityRow skill => ShipByUid(skill.ShipUid),
            EquipmentEquippedShipRow equippedShip => ShipByUid(equippedShip.ShipUid),
            AcademyRoomRow academyRoom => ShipByUid(academyRoom.ShipUid),
            _ => null
        };
    }

    private ShipRow? ShipByUid(string? shipUid)
    {
        return !string.IsNullOrWhiteSpace(shipUid) && shipsByUid.TryGetValue(shipUid, out var ship) ? ship : null;
    }

    private string ShipSearchText(object row)
    {
        return row switch
        {
            ShipRow ship => ship.SearchText,
            LevelPriorityRow level => level.SearchText,
            GearGapRow gap => gap.SearchText,
            FleetDamageRow damage => damage.SearchText,
            _ => ""
        };
    }

    private string ShipDisplayName(object row)
    {
        return row switch
        {
            ShipRow ship => ship.DisplayName,
            LevelPriorityRow level => level.DisplayName,
            GearGapRow gap => gap.DisplayName,
            FleetDamageRow damage => damage.ShipName,
            _ => SourceShip(row)?.DisplayName ?? ""
        };
    }

    private int ShipScore(object row)
    {
        return row switch
        {
            LevelPriorityRow level => level.Score,
            _ => 0
        };
    }

    private int ShipLevel(object row)
    {
        return row switch
        {
            ShipRow ship => ship.Level,
            LevelPriorityRow level => level.Level,
            GearGapRow gap => gap.Level,
            FleetDamageRow damage => damage.ShipLevel,
            _ => SourceShip(row)?.Level ?? 0
        };
    }

    private int LevelsTo120(object row)
    {
        return row is LevelPriorityRow level ? level.LevelsTo120 : Math.Max(0, 120 - ShipLevel(row));
    }

    private int LevelsTo125(object row)
    {
        return row is LevelPriorityRow level ? level.LevelsTo125 : Math.Max(0, 125 - ShipLevel(row));
    }

    private double ExpPercent(object row)
    {
        return row switch
        {
            ShipRow ship => ship.ExpPercent,
            LevelPriorityRow level => level.ExpPercent,
            _ => SourceShip(row)?.ExpPercent ?? 0
        };
    }

    private int? RunsTo120Avg(object row)
    {
        return row switch
        {
            ShipRow ship => ship.RunsTo120Avg,
            LevelPriorityRow level => level.RunsTo120Avg,
            _ => SourceShip(row)?.RunsTo120Avg
        };
    }

    private int? RunsTo125Avg(object row)
    {
        return row switch
        {
            ShipRow ship => ship.RunsTo125Avg,
            LevelPriorityRow level => level.RunsTo125Avg,
            _ => SourceShip(row)?.RunsTo125Avg
        };
    }

    private int ShipReload(object row)
    {
        return row switch
        {
            ShipRow ship => ship.Reload,
            FleetDamageRow damage => damage.ShipReload,
            _ => SourceShip(row)?.Reload ?? 0
        };
    }

    private int ShipMoodValue(object row)
    {
        return row switch
        {
            ShipRow ship => ship.MoodValue,
            LevelPriorityRow level => level.MoodValue,
            _ => SourceShip(row)?.MoodValue ?? 0
        };
    }

    private int ShipCreateTime(object row)
    {
        return row switch
        {
            ShipRow ship => ship.CreateTime,
            LevelPriorityRow level => level.CreateTime,
            _ => SourceShip(row)?.CreateTime ?? 0
        };
    }

    private string ShipRarity(object row)
    {
        return row switch
        {
            ShipRow ship => ship.Rarity,
            LevelPriorityRow level => level.Rarity,
            GearGapRow gap => gap.Rarity,
            FleetDamageRow damage => damage.ShipRarity,
            _ => SourceShip(row)?.Rarity ?? ""
        };
    }

    private string ShipRarityTier(object row)
    {
        return row switch
        {
            ShipRow ship => ship.RarityTier,
            LevelPriorityRow level => level.RarityTier,
            GearGapRow gap => gap.RarityTier,
            FleetDamageRow damage => damage.ShipRarityTier,
            _ => RarityFilterTier(SourceShip(row)?.Rarity ?? "")
        };
    }

    private int ShipTypeId(object row)
    {
        return row switch
        {
            ShipRow ship => ship.ShipTypeId,
            LevelPriorityRow level => level.ShipTypeId,
            GearGapRow gap => gap.ShipTypeId,
            FleetDamageRow damage => damage.ShipTypeId,
            _ => SourceShip(row)?.ShipTypeId ?? 0
        };
    }

    private string ShipTypeLabel(object row)
    {
        return row switch
        {
            ShipRow ship => ship.ShipTypeLabel,
            LevelPriorityRow level => level.ShipTypeLabel,
            GearGapRow gap => gap.ShipTypeLabel,
            FleetDamageRow damage => damage.ShipTypeLabel,
            _ => SourceShip(row)?.ShipTypeLabel ?? ""
        };
    }

    private string ShipTypeGroup(object row)
    {
        return row switch
        {
            ShipRow ship => ship.ShipTypeGroup,
            LevelPriorityRow level => level.ShipTypeGroup,
            GearGapRow gap => gap.ShipTypeGroup,
            FleetDamageRow damage => damage.ShipTypeGroup,
            _ => SourceShip(row)?.ShipTypeGroup ?? ""
        };
    }

    private string NationLabel(object row)
    {
        return row switch
        {
            ShipRow ship => ship.NationLabel,
            LevelPriorityRow level => level.NationLabel,
            GearGapRow gap => gap.NationLabel,
            FleetDamageRow damage => damage.NationLabel,
            _ => SourceShip(row)?.NationLabel ?? ""
        };
    }

    private int CommunityScore(object row)
    {
        return row switch
        {
            ShipRow ship => ship.CommunityScore,
            LevelPriorityRow level => level.CommunityScore,
            _ => SourceShip(row)?.CommunityScore ?? 0
        };
    }

    private int CommunityBestScore(object row)
    {
        return row switch
        {
            ShipRow ship => ship.CommunityBestScore,
            LevelPriorityRow level => level.CommunityBestScore,
            _ => SourceShip(row)?.CommunityBestScore ?? 0
        };
    }

    private bool FleetTechLevelPending(object row)
    {
        return row switch
        {
            ShipRow ship => ship.FleetTechLevelPending,
            LevelPriorityRow level => level.FleetTechLevelPending,
            _ => SourceShip(row)?.FleetTechLevelPending ?? false
        };
    }

    private bool FleetTechLevelReloadPending(object row)
    {
        return row switch
        {
            ShipRow ship => ship.FleetTechLevelReloadPending,
            LevelPriorityRow level => level.FleetTechLevelReloadPending,
            _ => SourceShip(row)?.FleetTechLevelReloadPending ?? false
        };
    }

    private int SkillGap(object row)
    {
        return SourceShip(row)?.SkillGap ?? 0;
    }

    private int EmptySlots(object row)
    {
        return row switch
        {
            ShipRow ship => ship.EmptySlots,
            GearGapRow gap => gap.EmptySlots,
            _ => SourceShip(row)?.EmptySlots ?? 0
        };
    }

    private bool Locked(object row)
    {
        return SourceShip(row)?.Locked ?? false;
    }

    private int GearRecommendationOwned(object row)
    {
        return SourceShip(row)?.GearRecommendationOwned ?? 0;
    }

    private int GearRecommendationTotal(object row)
    {
        return SourceShip(row)?.GearRecommendationTotal ?? 0;
    }

    private int AugmentRecommendationOwned(object row)
    {
        return SourceShip(row)?.AugmentRecommendationOwned ?? 0;
    }

    private int AugmentRecommendationTotal(object row)
    {
        return SourceShip(row)?.AugmentRecommendationTotal ?? 0;
    }

    private void RefreshShipViews()
    {
        SynchronizeSearchQuery();
        ShipsView.Refresh();
        FleetPlanner.RefreshQueueFilter();
        GearGapsView.Refresh();
        FleetDamageView.Refresh();
    }

    private void RebuildShipLookup()
    {
        shipsByUid.Clear();
        foreach (var ship in Ships)
        {
            if (!string.IsNullOrWhiteSpace(ship.ShipUid))
            {
                shipsByUid[ship.ShipUid] = ship;
            }
        }

        OpenShipDetailCommand.RaiseCanExecuteChanged();
    }
}
