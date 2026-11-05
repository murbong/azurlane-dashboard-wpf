using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private sealed class OwnedRecommendationItem
    {
        public int TemplateId { get; init; }
        public string Name { get; init; } = "";
        public int Count { get; init; }
        public int EnhanceLevel { get; init; }
        public int RarityScore { get; init; }
    }

    private sealed class RegalRecommendationItem
    {
        public string Kind { get; init; } = "";
        public string Usecase { get; init; } = "";
        public int Slot { get; init; }
        public int RowRank { get; set; }
        public int EquipRank { get; set; }
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string LocalName { get; init; } = "";
        public string Condition { get; init; } = "";
        public string Note { get; init; } = "";
        public string Rarity { get; init; } = "";
        public string RarityClass { get; init; } = "";
        public string IconUrl { get; init; } = "";
        public bool Owned { get; set; }
        public int OwnedCount { get; set; }
        public int OwnedEnhanceLevel { get; set; }
    }

    private sealed class MergedRecommendation
    {
        public string Kind { get; init; } = "";
        public int Slot { get; init; }
        public int Id { get; init; }
        public string Name { get; set; } = "";
        public string LocalName { get; set; } = "";
        public string IconUrl { get; set; } = "";
        public string Rarity { get; set; } = "";
        public string RarityClass { get; set; } = "";
        public bool Owned { get; set; }
        public int OwnedCount { get; set; }
        public int OwnedEnhanceLevel { get; set; }
        public int RowRank { get; set; }
        public int EquipRank { get; set; }
        public HashSet<string> Usecases { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Conditions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Notes { get; } = new(StringComparer.Ordinal);
    }

    private sealed class GearShortageAccumulator
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string IconUrl { get; init; } = "";
        public string Rarity { get; init; } = "";
        public int RarityScore { get; init; }
        public int MissingForCount { get; set; }
        public int Score { get; set; }
        public HashSet<string> ShipUids { get; } = new(StringComparer.Ordinal);
        public List<string> TopShipNames { get; } = new();
    }

}
