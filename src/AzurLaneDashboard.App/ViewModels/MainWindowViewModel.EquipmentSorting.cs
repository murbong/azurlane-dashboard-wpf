using System.Collections;
using System.ComponentModel;
using System.Windows.Data;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void ToggleEquipmentSortDirection()
    {
        isEquipmentSortAscending = !isEquipmentSortAscending;
        OnPropertyChanged(nameof(EquipmentSortDirectionLabel));
        OnPropertyChanged(nameof(EquipmentSortDirectionToolTip));
        ApplyEquipmentSort();
        RefreshEquipmentViews();
        SaveSettings();
    }

    public void ApplyEquipmentGridSort(string sortOption, bool ascending)
    {
        selectedEquipmentSortOption = string.IsNullOrWhiteSpace(sortOption) ? "레어도" : sortOption;
        isEquipmentSortAscending = ascending;
        OnPropertyChanged(nameof(SelectedEquipmentSortOption));
        OnPropertyChanged(nameof(EquipmentSortDirectionLabel));
        OnPropertyChanged(nameof(EquipmentSortDirectionToolTip));
        ApplyEquipmentSort();
        RefreshEquipmentViews();
        NotifyCounts();
        SaveSettings();
    }

    private void ApplyEquipmentSort()
    {
        ApplyEquipmentSort(EquipmentView);
        ApplyEquipmentSort(EquipmentRecommendationsView);
    }

    private void ApplyEquipmentSort(ICollectionView view)
    {
        if (view is ListCollectionView listView)
        {
            listView.CustomSort = new EquipmentRowComparer(this);
        }
    }

    private int CompareEquipmentRows(EquipmentRow? left, EquipmentRow? right)
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

        var comparison = SelectedEquipmentSortOption switch
        {
            "점수" => FirstNonZero(
                CompareNumberAsc(left.Score, right.Score),
                CompareTextAsc(left.Name, right.Name)),
            "강화" => FirstNonZero(
                CompareNumberAsc(left.EnhanceLevel, right.EnhanceLevel),
                CompareNumberAsc(left.Count, right.Count),
                CompareTextAsc(left.Name, right.Name)),
            "개수" => FirstNonZero(
                CompareNumberAsc(left.Count, right.Count),
                CompareTextAsc(left.Name, right.Name)),
            "창고" => FirstNonZero(
                CompareNumberAsc(left.BagCount, right.BagCount),
                CompareNumberAsc(left.Count, right.Count),
                CompareTextAsc(left.Name, right.Name)),
            "장착" => FirstNonZero(
                CompareNumberAsc(left.EquippedCount, right.EquippedCount),
                CompareNumberAsc(left.Count, right.Count),
                CompareTextAsc(left.Name, right.Name)),
            "이름" => CompareTextAsc(left.Name, right.Name),
            _ => FirstNonZero(
                CompareNumberAsc(RarityScore(left.RarityTier), RarityScore(right.RarityTier)),
                CompareNumberAsc(left.EnhanceLevel, right.EnhanceLevel),
                CompareNumberAsc(left.Count, right.Count),
                CompareTextAsc(left.Name, right.Name))
        };

        return isEquipmentSortAscending ? comparison : -comparison;
    }

    private void RefreshEquipmentViews()
    {
        SynchronizeSearchQuery();
        EquipmentView.Refresh();
        EquipmentRecommendationsView.Refresh();
        SpecialEquipmentView.Refresh();
        EquipmentSkinsView.Refresh();
        CommunityGearShortagesView.Refresh();
    }

    private sealed class EquipmentRowComparer(MainWindowViewModel owner) : IComparer
    {
        public int Compare(object? left, object? right)
        {
            return owner.CompareEquipmentRows(left as EquipmentRow, right as EquipmentRow);
        }
    }
}
