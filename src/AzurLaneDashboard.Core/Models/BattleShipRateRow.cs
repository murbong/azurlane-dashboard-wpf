namespace AzurLaneDashboard.Core.Models;

public sealed class BattleShipRateRow
{
    public string ShipUid { get; init; } = "";
    public string Name { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string RarityTier { get; init; } = "";
    public string RarityClass { get; init; } = "";
    public bool IsProposed { get; init; }
    public string ShipDisplayName => Name;
    public string ShipDisplayIconUrl => IconUrl;
    public string ShipDisplaySummary => ShipMetaSummary;
    public int Level { get; init; }
    public int SampleExp { get; init; }
    public int AvgExp { get; init; }
    public int LastExp { get; init; }
    public int MvpCount { get; init; }
    public int? RunsTo120Avg { get; init; }
    public int? RunsTo125Avg { get; init; }
    public string SampleKind { get; init; } = "";
    public int ExpCurrent { get; init; }
    public int ExpRequired { get; init; }
    public double ExpPercent { get; init; }
    public string ExpProgressText => ExpRequired <= 0 ? "-" : $"{ExpCurrent:N0} / {ExpRequired:N0}";
    public string RunsTo120Text => RunsTo120Avg is null
        ? "-"
        : RunsTo120Avg == 0
        ? "완료"
        : $"{RunsTo120Avg:N0}{(SampleKind == "stage_clear" ? "스테이지" : "판")}";

    public string RunsTo125Text => RunsTo125Avg is null
        ? "-"
        : RunsTo125Avg == 0
        ? "완료"
        : $"{RunsTo125Avg:N0}{(SampleKind == "stage_clear" ? "스테이지" : "판")}";
    public int MoodValue { get; init; }
    public string MoodLabel { get; init; } = "";
    public string MoodText => MoodValue > 0 ? $"{MoodValue:N0}" : "-";

    public string ShipMetaSummary => string.Join(" · ",
        new[] { $"Lv.{Level:N0}", MoodValue > 0 ? $"{MoodLabel} {MoodText}" : "" }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
}
