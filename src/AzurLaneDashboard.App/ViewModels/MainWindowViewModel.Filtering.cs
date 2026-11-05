using System.Collections;
using System.ComponentModel;
using System.Linq;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void ScheduleSearchRefresh()
    {
        if (isDisposed)
        {
            return;
        }

        searchRefreshTimer.Stop();
        searchRefreshTimer.Start();
    }

    private void SearchRefreshTimerTick(object? sender, EventArgs e)
    {
        searchRefreshTimer.Stop();
        ApplySearchRefresh();
    }

    private void ApplySearchRefresh()
    {
        var normalized = NormalizeSearchQuery(query);
        if (string.Equals(appliedQuery, normalized, StringComparison.Ordinal))
        {
            return;
        }

        appliedQuery = normalized;
        RefreshShipViews();
        RefreshEquipmentViews();
        NotifyCounts();
    }

    private void SynchronizeSearchQuery()
    {
        appliedQuery = NormalizeSearchQuery(query);
    }

    private static string NormalizeSearchQuery(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant();
    }

    private static int CountView(ICollectionView view)
    {
        return view is ICollection collection ? collection.Count : view.Cast<object>().Count();
    }

    private bool FilterShip(object item)
    {
        return item is ShipRow ship && MatchesShipRow(ship);
    }

    private bool FilterGearGap(object item)
    {
        return item is GearGapRow row && MatchesShipRow(row);
    }

    private bool FilterFleetDamage(object item)
    {
        return item is FleetDamageRow row && MatchesShipRow(row);
    }

    private bool FilterEquipment(object item)
    {
        if (item is not EquipmentRow equipment)
        {
            return false;
        }

        var text = appliedQuery;
        return (text.Length == 0 || equipment.SearchText.Contains(text))
               && MatchesRarity(equipment.RarityTier)
               && MatchesEquipmentType(equipment)
               && MatchesEquipmentExtra(equipment);
    }

    private bool FilterSpecialEquipment(object item)
    {
        if (item is not SpecialEquipmentRow equipment)
        {
            return false;
        }

        var text = appliedQuery;
        return (text.Length == 0 || equipment.SearchText.Contains(text))
               && MatchesRarity(equipment.RarityTier);
    }

    private bool FilterEquipmentSkin(object item)
    {
        if (item is not EquipmentSkinRow skin)
        {
            return false;
        }

        var text = appliedQuery;
        return (text.Length == 0 || skin.SearchText.Contains(text))
               && MatchesRarity(skin.RarityTier);
    }

    private bool FilterCommunityGearShortage(object item)
    {
        if (item is not CommunityGearShortageRow shortage)
        {
            return false;
        }

        var text = appliedQuery;
        return (text.Length == 0 || shortage.SearchText.Contains(text))
               && MatchesRarity(shortage.RarityTier);
    }

    private bool MatchesEquipmentType(EquipmentRow equipment)
    {
        return SelectedEquipmentTypeFilter switch
        {
            null or "" or "전체" => true,
            "함포" => equipment.EquipmentTypeGroup == "gun",
            "항공" => equipment.EquipmentTypeGroup == "aircraft",
            "어뢰" => equipment.EquipmentTypeGroup == "torpedo",
            "대공" => equipment.EquipmentTypeGroup == "antiair",
            "설비" => equipment.EquipmentTypeGroup == "auxiliary",
            "대잠" => equipment.EquipmentTypeGroup == "asw",
            "미사일" => equipment.EquipmentTypeGroup == "missile",
            "기타" => equipment.EquipmentTypeGroup is not ("gun" or "aircraft" or "torpedo" or "antiair" or "auxiliary"
                or "asw" or "missile"),
            _ => true
        };
    }

    private bool MatchesEquipmentExtra(EquipmentRow equipment)
    {
        return SelectedEquipmentExtraFilter switch
        {
            "강화 필요" => equipment.EnhanceLevel < 13,
            "+10~12" => equipment.EnhanceLevel >= 10 && equipment.EnhanceLevel < 13,
            "+13 완료" => equipment.EnhanceLevel >= 13,
            "복수 보유" => equipment.Count >= 2,
            _ => true
        };
    }

    private void NotifyCounts()
    {
        filteredShipCount = CountView(ShipsView);
        filteredEquipmentCount = CountView(EquipmentView);
        filteredFleetDamageCount = CountView(FleetDamageView);
        filteredEquipmentRecommendationCount = CountView(EquipmentRecommendationsView);
        filteredSpecialEquipmentCount = CountView(SpecialEquipmentView);
        filteredEquipmentSkinCount = CountView(EquipmentSkinsView);
        filteredCommunityGearShortageCount = CountView(CommunityGearShortagesView);
        OnPropertyChanged(nameof(ShipCount));
        OnPropertyChanged(nameof(EquipmentCount));
        OnPropertyChanged(nameof(FilteredShipCount));
        OnPropertyChanged(nameof(FilteredEquipmentCount));
        OnPropertyChanged(nameof(FilteredFleetDamageCount));
        OnPropertyChanged(nameof(DockUsageText));
        OnPropertyChanged(nameof(EquipmentUsageText));
        OnPropertyChanged(nameof(FleetDamageUsageText));
        OnPropertyChanged(nameof(FilteredEquipmentRecommendationText));
        OnPropertyChanged(nameof(FilteredSpecialEquipmentText));
        OnPropertyChanged(nameof(FilteredEquipmentSkinText));
        OnPropertyChanged(nameof(FilteredCommunityGearShortageText));
        ExportWorkbookCommand.RaiseCanExecuteChanged();
    }

    private void RefreshShipFilterOptions()
    {
        if (!ShipTypeFilters.Contains(SelectedShipTypeFilter))
        {
            SelectedShipTypeFilter = "전체";
        }

        if (!NationFilters.Contains(SelectedNationFilter))
        {
            SelectedNationFilter = "전체";
        }
    }
}
