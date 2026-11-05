using System.Globalization;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static List<ShipRecommendationGroupRow> BuildRecommendationGroups(
        List<RegalRecommendationItem> rows,
        int maxPerSlot)
    {
        var merged = MergeRecommendations(rows, includeSlot: true);
        return merged
            .GroupBy(item => item.Slot)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var original = group.OrderBy(item => item,
                        Comparer<MergedRecommendation>.Create(CompareRecommendationRank))
                    .ToList();
                var rankById = original.Select((item, index) => new { item.Id, Rank = index + 1 })
                    .ToDictionary(item => item.Id, item => item.Rank);
                var sorted = original
                    .OrderBy(item => item.Owned ? 0 : 1)
                    .ThenBy(item => item, Comparer<MergedRecommendation>.Create(CompareRecommendationRank))
                    .ToList();
                var shown = sorted.Take(maxPerSlot)
                    .Select(item => ToRecommendationRow(item, rankById.GetValueOrDefault(item.Id)))
                    .ToList();
                return new ShipRecommendationGroupRow
                {
                    Slot = group.Key,
                    OwnedCount = sorted.Count(item => item.Owned),
                    TotalCount = sorted.Count,
                    HiddenCount = Math.Max(0, sorted.Count - shown.Count),
                    Items = shown
                };
            })
            .Where(group => group.Items.Count > 0)
            .ToList();
    }

    private static List<ShipRecommendationRow> BuildFlatRecommendationRows(List<RegalRecommendationItem> rows,
        int limit)
    {
        var merged = MergeRecommendations(rows, includeSlot: false)
            .OrderBy(item => item, Comparer<MergedRecommendation>.Create(CompareRecommendationRank))
            .ToList();
        var rankById = merged.Select((item, index) => new { item.Id, Rank = index + 1 })
            .ToDictionary(item => item.Id, item => item.Rank);
        return merged
            .OrderBy(item => item.Owned ? 0 : 1)
            .ThenBy(item => item, Comparer<MergedRecommendation>.Create(CompareRecommendationRank))
            .Take(limit)
            .Select(item => ToRecommendationRow(item, rankById.GetValueOrDefault(item.Id)))
            .ToList();
    }

    private static List<MergedRecommendation> MergeRecommendations(List<RegalRecommendationItem> rows, bool includeSlot)
    {
        var merged = new Dictionary<string, MergedRecommendation>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = includeSlot ? $"{row.Slot}:{row.Id}" : row.Id.ToString(CultureInfo.InvariantCulture);
            if (!merged.TryGetValue(key, out var current))
            {
                current = new MergedRecommendation
                {
                    Kind = row.Kind,
                    Slot = includeSlot ? row.Slot : 0,
                    Id = row.Id,
                    Name = row.Name,
                    LocalName = row.LocalName,
                    IconUrl = row.IconUrl,
                    Rarity = row.Rarity,
                    RarityClass = row.RarityClass,
                    Owned = row.Owned,
                    OwnedCount = row.OwnedCount,
                    OwnedEnhanceLevel = row.OwnedEnhanceLevel,
                    RowRank = row.RowRank,
                    EquipRank = row.EquipRank
                };
                AddRecommendationMetadata(current, row);
                merged[key] = current;
                continue;
            }

            AddRecommendationMetadata(current, row);
            current.Owned |= row.Owned;
            current.OwnedCount = Math.Max(current.OwnedCount, row.OwnedCount);
            current.OwnedEnhanceLevel = Math.Max(current.OwnedEnhanceLevel, row.OwnedEnhanceLevel);
            if (CompareRecommendationRank(row, current) < 0)
            {
                current.RowRank = row.RowRank;
                current.EquipRank = row.EquipRank;
            }
        }

        return merged.Values.ToList();
    }

    private static void AddRecommendationMetadata(MergedRecommendation target, RegalRecommendationItem source)
    {
        if (!string.IsNullOrWhiteSpace(source.Usecase))
        {
            target.Usecases.Add(source.Usecase);
        }

        if (!string.IsNullOrWhiteSpace(source.Condition))
        {
            target.Conditions.Add(source.Condition);
        }

        if (!string.IsNullOrWhiteSpace(source.Note))
        {
            target.Notes.Add(source.Note);
        }
    }

    private static ShipRecommendationRow ToRecommendationRow(MergedRecommendation item, int rank)
    {
        var usecases = item.Usecases
            .OrderBy(UsecaseRank)
            .ThenBy(value => value, StringComparer.CurrentCulture)
            .ToList();
        return new ShipRecommendationRow
        {
            Kind = item.Kind,
            Id = item.Id.ToString(CultureInfo.InvariantCulture),
            Name = item.Name,
            LocalName = item.LocalName,
            IconUrl = item.IconUrl,
            Rarity = item.Rarity,
            RarityClass = item.RarityClass,
            Owned = item.Owned,
            OwnedCount = item.OwnedCount,
            OwnedEnhanceLevel = item.OwnedEnhanceLevel,
            OriginalRank = rank,
            UsecaseSummary = string.Join("/", usecases),
            ConditionSummary = string.Join(" · ", item.Conditions),
            NoteSummary = item.Notes.FirstOrDefault() ?? ""
        };
    }
}
