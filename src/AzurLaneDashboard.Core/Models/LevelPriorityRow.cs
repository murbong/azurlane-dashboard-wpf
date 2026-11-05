namespace AzurLaneDashboard.Core.Models;

public sealed class LevelPriorityRow
{
    private string? searchText;

    public int Score { get; init; }
    public string ShipUid { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string RarityTier { get; init; } = "";
    public string RarityClass { get; init; } = "";
    public int ShipTypeId { get; init; }
    public string ShipTypeLabel { get; init; } = "";
    public string ShipTypeGroup { get; init; } = "";
    public int NationId { get; init; }
    public string NationLabel { get; init; } = "";
    public bool IsProposed { get; init; }
    public string ShipDisplayName => DisplayName;
    public string ShipDisplayIconUrl => IconUrl;
    public string ShipDisplaySummary => ShipMetaSummary;

    public string ShipMetaSummary => string.Join(" · ",
        new[] { ShipTypeLabel, NationLabel }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public int Level { get; init; }
    public int LevelsTo120 { get; init; }
    public int LevelsTo125 { get; init; }
    public int ExpCurrent { get; init; }
    public int ExpRequired { get; init; }
    public double ExpPercent { get; init; }
    public bool ExpIsMax { get; init; }
    public string ExpProgressText
    {
        get
        {
            if (ExpIsMax || ExpRequired <= 0)
            {
                return ExpIsMax ? "Max" : "-";
            }

            return $"{ExpCurrent:N0} / {ExpRequired:N0}";
        }
    }

    public string RunsTo120Text => RunsTo120Avg is null ? "-" : RunsTo120Avg == 0 ? "완료" : $"{RunsTo120Avg:N0}스테이지";
    public string RunsTo125Text => RunsTo125Avg is null ? "-" : RunsTo125Avg == 0 ? "완료" : $"{RunsTo125Avg:N0}스테이지";
    public int MoodValue { get; init; }
    public string MoodLabel { get; init; } = "";
    public string MoodText => MoodValue > 0 ? $"{MoodValue:N0}" : "-";
    public int CreateTime { get; init; }

    public string CreateTimeText
    {
        get
        {
            var value = CreateTime;
            if (value <= 0)
            {
                return "-";
            }

            return DateTimeOffset.FromUnixTimeSeconds(value).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        }
    }

    public string SkillSummary { get; init; } = "";
    public string FleetTechLevelBonus { get; init; } = "";
    public bool FleetTechLevelPending { get; init; }
    public bool FleetTechLevelReloadPending { get; init; }
    public string CommunityLabel { get; init; } = "";
    public string CommunitySummary { get; init; } = "";
    public int CommunityScore { get; init; }
    public int CommunityBestScore { get; init; }
    public IReadOnlyList<ShipCommunityRatingRow> CommunityRatings { get; init; } = [];
    // One line of chips in list rows; the full list stays in the tooltip and detail panel.
    public IReadOnlyList<ShipCommunityRatingRow> CommunityRatingPreview => CommunityRatings.Take(3).ToList();

    public string CommunityKind => CommunityBestScore >= 34 ? "high" :
        CommunityBestScore >= 22 ? "mid" :
        CommunityBestScore > 0 ? "low" : "";

    public int? RunsTo120Avg { get; init; }
    public int? RunsTo125Avg { get; init; }
    public int RecentBattleExpAvg { get; init; }

    public string SearchText => searchText ??= $"{ShipUid} {DisplayName} {Rarity} {ShipTypeLabel} {NationLabel} {CommunityLabel}"
        .ToLowerInvariant();
}
