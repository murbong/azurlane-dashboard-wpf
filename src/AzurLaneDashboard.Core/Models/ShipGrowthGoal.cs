namespace AzurLaneDashboard.Core.Models;

public sealed record ShipGrowthGoal
{
    // Ship UIDs are only unique within a data source.
    public string DatabasePath { get; init; } = "";
    public string ShipUid { get; init; } = "";
    public string ShipName { get; init; } = "";
    public bool TrackLevel { get; init; } = true;
    public int TargetLevel { get; init; } = 120;
    public bool TrackSkills { get; init; } = true;
    public int TargetSkillLevel { get; init; } = 10;
}

public sealed record ShipGrowthPlan
{
    public required ShipGrowthGoal Goal { get; init; }
    public ShipRow? Ship { get; init; }
    public string ShipName => Ship?.DisplayName ?? Goal.ShipName;
    public string IconUrl => Ship?.IconUrl ?? "";
    public bool IsComplete { get; init; }
    public bool NeedsData { get; init; }
    public int? RemainingExperience { get; init; }
    public int? EstimatedBattles { get; init; }
    public int RemainingSkillLevels { get; init; }
    public string Status => IsComplete ? "목표 달성" : NeedsData ? "정보 확인 필요" : "육성 중";
    public string TargetSummary => string.Join(" · ", new[]
    {
        Goal.TrackLevel ? $"목표 Lv.{Goal.TargetLevel}" : "",
        Goal.TrackSkills ? $"훈련 가능한 스킬 Lv.{Goal.TargetSkillLevel}" : ""
    }.Where(value => value.Length > 0));
    public string LevelSummary { get; init; } = "";
    public string SkillSummary { get; init; } = "";
    public string EstimateSummary { get; init; } = "";
    public string NextAction { get; init; } = "";
    public string Evidence { get; init; } = "";
}
