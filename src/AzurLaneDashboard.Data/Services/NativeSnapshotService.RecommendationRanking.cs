using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static int CompareOwnedRecommendationRank(RegalRecommendationItem left, RegalRecommendationItem right)
    {
        if (left.Owned != right.Owned)
        {
            return left.Owned ? -1 : 1;
        }

        return CompareRecommendationRank(left, right);
    }

    private static int CompareRecommendationRank(RegalRecommendationItem left, MergedRecommendation right)
    {
        var row = left.RowRank.CompareTo(right.RowRank);
        if (row != 0) return row;
        var equip = left.EquipRank.CompareTo(right.EquipRank);
        if (equip != 0) return equip;
        var usecase = UsecaseRank(left.Usecase).CompareTo(UsecaseRank(right.Usecases.FirstOrDefault() ?? ""));
        if (usecase != 0) return usecase;
        return string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
    }

    private static int CompareRecommendationRank(RegalRecommendationItem left, RegalRecommendationItem right)
    {
        var row = left.RowRank.CompareTo(right.RowRank);
        if (row != 0) return row;
        var equip = left.EquipRank.CompareTo(right.EquipRank);
        if (equip != 0) return equip;
        var usecase = UsecaseRank(left.Usecase).CompareTo(UsecaseRank(right.Usecase));
        if (usecase != 0) return usecase;
        return string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
    }

    private static int CompareRecommendationRank(MergedRecommendation left, MergedRecommendation right)
    {
        var row = left.RowRank.CompareTo(right.RowRank);
        if (row != 0) return row;
        var equip = left.EquipRank.CompareTo(right.EquipRank);
        if (equip != 0) return equip;
        var usecase = UsecaseRank(left.Usecases.FirstOrDefault() ?? "")
            .CompareTo(UsecaseRank(right.Usecases.FirstOrDefault() ?? ""));
        if (usecase != 0) return usecase;
        return string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
    }

    private static int UsecaseRank(string value)
    {
        if (string.Equals(value, "C", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("C-", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return value switch
        {
            "L" => 2,
            "M" => 3,
            "H" => 4,
            _ => 9
        };
    }
}
