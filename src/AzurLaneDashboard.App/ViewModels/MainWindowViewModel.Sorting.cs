using System.Collections;
using System.ComponentModel;
using System.Windows.Data;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void ApplyShipSort()
    {
        ApplyShipSort(ShipsView, ShipSortMode.Dock);
        ApplyShipSort(GearGapsView, ShipSortMode.Level);
        ApplyShipSort(FleetDamageView, ShipSortMode.FleetDamage);
    }

    private void ApplyShipSort(ICollectionView view, ShipSortMode mode)
    {
        if (view is ListCollectionView listView)
        {
            listView.CustomSort = new ShipRowComparer(this, mode);
        }
    }

    private void ToggleShipSortDirection()
    {
        if (IsLevelTabActive)
        {
            isLevelShipSortAscending = !isLevelShipSortAscending;
            ApplyShipSort(GearGapsView, ShipSortMode.Level);
        }
        else if (IsFleetDamageTabActive)
        {
            isFleetDamageSortAscending = !isFleetDamageSortAscending;
            ApplyShipSort(FleetDamageView, ShipSortMode.FleetDamage);
        }
        else
        {
            isDockShipSortAscending = !isDockShipSortAscending;
            ApplyShipSort(ShipsView, ShipSortMode.Dock);
        }

        OnPropertyChanged(nameof(ShipSortDirectionLabel));
        OnPropertyChanged(nameof(ShipSortDirectionToolTip));
        RefreshShipViews();
        SaveSettings();
    }

    public void ApplyDockGridSort(string sortOption, bool ascending)
    {
        selectedDockShipSortOption = NormalizeShipSortOption(sortOption);
        isDockShipSortAscending = ascending;
        OnPropertyChanged(nameof(SelectedShipSortOption));
        OnPropertyChanged(nameof(ShipSortDirectionLabel));
        OnPropertyChanged(nameof(ShipSortDirectionToolTip));
        ApplyShipSort(ShipsView, ShipSortMode.Dock);
        RefreshShipViews();
        NotifyCounts();
        SaveSettings();
    }

    public void ApplyFleetDamageGridSort(string sortOption, bool ascending)
    {
        selectedFleetDamageSortOption = string.IsNullOrWhiteSpace(sortOption) || !FleetDamageSortOptions.Contains(sortOption)
            ? "함대"
            : sortOption;
        isFleetDamageSortAscending = ascending;
        OnPropertyChanged(nameof(SelectedShipSortOption));
        OnPropertyChanged(nameof(ShipSortDirectionLabel));
        OnPropertyChanged(nameof(ShipSortDirectionToolTip));
        ApplyShipSort(FleetDamageView, ShipSortMode.FleetDamage);
        RefreshShipViews();
        NotifyCounts();
        SaveSettings();
    }


    private int CompareShipRows(object? left, object? right, ShipSortMode mode)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return 1;
        }

        if (right is null)
        {
            return -1;
        }

        // Gaps on sortie-fleet ships stay pinned above stored ships, grouped by fleet, whatever the sort.
        if (left is GearGapRow leftGap && right is GearGapRow rightGap)
        {
            var pinned = CompareNumberAsc(GearGapFleetOrder(leftGap), GearGapFleetOrder(rightGap));
            if (pinned != 0)
            {
                return pinned;
            }
        }

        var sort = ShipSortStateFor(mode);
        var comparison = mode == ShipSortMode.FleetDamage
            ? CompareFleetDamageRows(left, right, sort.Option)
            : CompareStandardShipRows(left, right, sort.Option);

        return sort.Ascending ? comparison : -comparison;
    }

    private ShipSortState ShipSortStateFor(ShipSortMode mode)
    {
        return mode switch
        {
            ShipSortMode.Level => new ShipSortState(selectedLevelShipSortOption, isLevelShipSortAscending),
            ShipSortMode.FleetDamage => new ShipSortState(selectedFleetDamageSortOption, isFleetDamageSortAscending),
            _ => new ShipSortState(selectedDockShipSortOption, isDockShipSortAscending)
        };
    }

    private int CompareStandardShipRows(object left, object right, string? sortOption)
    {
        return NormalizeShipSortOption(sortOption) switch
        {
            "우선순위" => FirstNonZero(
                CompareNumberAsc(ShipScore(left), ShipScore(right)),
                CompareNumberAsc(ShipLevel(left), ShipLevel(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right))),
            "커뮤니티" => FirstNonZero(
                CompareNumberAsc(CommunityScore(left), CommunityScore(right)),
                CompareNumberAsc(ShipLevel(left), ShipLevel(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right))),
            "레어도" => FirstNonZero(
                CompareNumberAsc(RarityScore(ShipRarity(left)), RarityScore(ShipRarity(right))),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right))),
            "함종" => FirstNonZero(
                CompareNumberAsc(ShipTypeId(left), ShipTypeId(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right))),
            "진영" => FirstNonZero(
                CompareTextAsc(NationLabel(left), NationLabel(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right))),
            "기술" => FirstNonZero(
                CompareNumberAsc(FleetTechLevelReloadPending(left) ? 1 : 0,
                    FleetTechLevelReloadPending(right) ? 1 : 0),
                CompareNumberAsc(FleetTechLevelPending(left) ? 1 : 0, FleetTechLevelPending(right) ? 1 : 0),
                CompareNumberAsc(ShipScore(left), ShipScore(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right))),
            "120까지" => FirstNonZero(
                CompareNumberAsc(LevelsTo120(left), LevelsTo120(right)),
                CompareNumberAsc(ShipLevel(left), ShipLevel(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right))),
            "125까지" => FirstNonZero(
                CompareNumberAsc(LevelsTo125(left), LevelsTo125(right)),
                CompareNumberAsc(ShipLevel(left), ShipLevel(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right))),
            "EXP" => FirstNonZero(
                CompareDoubleAsc(ExpPercent(left), ExpPercent(right)),
                CompareNumberAsc(ShipLevel(left), ShipLevel(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right))),
            "120 예상" => FirstNonZero(
                CompareNullableNumberAsc(RunsTo120Avg(left), RunsTo120Avg(right)),
                CompareNumberAsc(ShipLevel(left), ShipLevel(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right))),
            "125 예상" => FirstNonZero(
                CompareNullableNumberAsc(RunsTo125Avg(left), RunsTo125Avg(right)),
                CompareNumberAsc(ShipLevel(left), ShipLevel(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right))),
            "기분" => FirstNonZero(
                CompareNumberAsc(ShipMoodValue(left), ShipMoodValue(right)),
                CompareNumberAsc(ShipLevel(left), ShipLevel(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right))),
            "입수시간" => FirstNonZero(
                CompareNumberAsc(ShipCreateTime(left), ShipCreateTime(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right))),
            "이름" => CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right)),
            _ => FirstNonZero(
                CompareNumberAsc(ShipLevel(left), ShipLevel(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right)))
        };
    }

    private int CompareFleetDamageRows(object left, object right, string? sortOption)
    {
        return sortOption switch
        {
            "함대" => FirstNonZero(
                CompareNumberAsc(FleetDamageFleetId(left), FleetDamageFleetId(right)),
                CompareNumberAsc(FleetDamageFleetSlot(left), FleetDamageFleetSlot(right)),
                CompareNumberAsc(FleetDamageFormationSort(left), FleetDamageFormationSort(right)),
                CompareNumberAsc(FleetDamageSlot(left), FleetDamageSlot(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right))),
            "쿨다운" => FirstNonZero(
                CompareDoubleAsc(FleetDamageReloadSeconds(left), FleetDamageReloadSeconds(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right)),
                CompareNumberAsc(FleetDamageSlot(left), FleetDamageSlot(right))),
            "사속" => FirstNonZero(
                CompareDoubleAsc(FleetDamageFireRate(left), FleetDamageFireRate(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right)),
                CompareNumberAsc(FleetDamageSlot(left), FleetDamageSlot(right))),
            "장전" => FirstNonZero(
                CompareNumberAsc(ShipReload(left), ShipReload(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right)),
                CompareNumberAsc(FleetDamageSlot(left), FleetDamageSlot(right))),
            "함선" => FirstNonZero(
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right)),
                CompareNumberAsc(FleetDamageSlot(left), FleetDamageSlot(right))),
            "슬롯" => FirstNonZero(
                CompareNumberAsc(FleetDamageSlot(left), FleetDamageSlot(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right))),
            _ => FirstNonZero(
                CompareDoubleAsc(FleetDamageReloadSeconds(left), FleetDamageReloadSeconds(right)),
                CompareTextAsc(ShipDisplayName(left), ShipDisplayName(right)),
                CompareNumberAsc(FleetDamageSlot(left), FleetDamageSlot(right)))
        };
    }


    private static int RarityScore(string rarity)
    {
        return RarityFilterTier(rarity) switch
        {
            "UR" => 70,
            "SSR" => 52,
            "SR" => 32,
            "R" => 16,
            "N" => 4,
            _ => 0
        };
    }

    private static string RarityFilterTier(string rarity)
    {
        return RarityHelper.Normalize(rarity) switch
        {
            "DR" or "UR" => "UR",
            "PR" or "SSR" => "SSR",
            "SR" => "SR",
            "R" => "R",
            "N" => "N",
            _ => ""
        };
    }

    private static string NormalizeShipSortOption(string? value)
    {
        return value switch
        {
            "레벨" => "Lv",
            "120 기술" => "기술",
            "획득시간" or "획득" or "입수" => "입수시간",
            _ => string.IsNullOrWhiteSpace(value) ? "Lv" : value
        };
    }

    private static int CompareNumberAsc(int left, int right)
    {
        return left.CompareTo(right);
    }

    private static int GearGapFleetOrder(GearGapRow row) => row.FleetOrder > 0 ? row.FleetOrder : int.MaxValue;

    private static int CompareDoubleAsc(double left, double right)
    {
        return left.CompareTo(right);
    }

    private static int CompareNullableNumberAsc(int? left, int? right)
    {
        return (left ?? int.MaxValue).CompareTo(right ?? int.MaxValue);
    }

    private static int CompareTextAsc(string? left, string? right)
    {
        return string.Compare(left ?? "", right ?? "", StringComparison.CurrentCulture);
    }

    private static double FleetDamageDps(object row)
    {
        return row is FleetDamageRow damage ? damage.EstimatedDps : 0;
    }

    private static int FleetDamageFleetId(object row)
    {
        return row is FleetDamageRow damage ? damage.FleetId : 0;
    }

    private static int FleetDamageFleetSlot(object row)
    {
        return row is FleetDamageRow damage
            ? damage.FleetSlot > 0 ? damage.FleetSlot : 99
            : 0;
    }

    private static int FleetDamageFormationSort(object row)
    {
        return row is FleetDamageRow damage
            ? damage.FormationGroup switch
            {
                "후열" => 0,
                "전열" => 1,
                "잠수" => 2,
                _ => 3
            }
            : 0;
    }

    private static double FleetDamageReloadSeconds(object row)
    {
        return row is FleetDamageRow damage ? damage.ReloadSeconds : 0;
    }

    private static double FleetDamageFireRate(object row)
    {
        return row is FleetDamageRow damage ? damage.FireRatePerMinute : 0;
    }

    private static int FleetDamageSlot(object row)
    {
        return row is FleetDamageRow damage
            ? damage.Slot > 0 ? damage.Slot : 99
            : 0;
    }

    private static int FirstNonZero(params int[] comparisons)
    {
        foreach (var comparison in comparisons)
        {
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }


    private void RefreshToolbarSortOptions()
    {
        var source = IsLevelTabActive
            ? LevelShipSortOptions
            : IsFleetDamageTabActive
                ? FleetDamageSortOptions
                : DockShipSortOptions;
        ShipSortOptions.Clear();
        foreach (var item in source)
        {
            ShipSortOptions.Add(item);
        }
    }

    private sealed class ShipRowComparer(MainWindowViewModel owner, ShipSortMode mode) : IComparer
    {
        public int Compare(object? left, object? right)
        {
            return owner.CompareShipRows(left, right, mode);
        }
    }

    private readonly record struct ShipSortState(string Option, bool Ascending);

    private enum ShipSortMode
    {
        Dock,
        Level,
        FleetDamage
    }
}
