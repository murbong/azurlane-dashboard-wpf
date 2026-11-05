using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;

internal static class GrowthPlannerChecks
{
    public static void Run()
    {
        using var document = JsonDocument.Parse("""
            {"118":{"exp":100,"exp_ur":120},"119":{"exp":200,"exp_ur":240},
             "120":{"exp":300,"exp_ur":360},"121":{"exp":400,"exp_ur":480}}
            """);
        var table = document.RootElement;
        int? Remaining(int level, int exp, int target, string rarity = "SSR") =>
            ShipExperienceCalculator.Remaining(level, exp, target,
                next => ShipExperienceCalculator.Required(table, next, rarity));
        Check(Remaining(118, 50, 120) == 250, "arbitrary target EXP");
        Check(Remaining(118, 50, 120, "DR") == 310, "DR uses UR EXP curve");
        Check(Remaining(120, 500, 122) == 200, "banked EXP crosses multiple levels");
        Check(Remaining(120, 900, 122) == 0, "banked EXP never produces negative remainder");
        Check(Remaining(120, -90, 122) == 700, "negative EXP is ignored");
        Check(Remaining(120, 0, 125) is null, "missing level row is unknown, not zero");
        Check(Remaining(0, 0, 120) is null && Remaining(1, 0, 126) is null, "invalid levels are rejected");
        Check(Remaining(125, 0, 125) == 0, "already completed level needs no table");
        using var malformed = JsonDocument.Parse("""{"1":{"exp":"missing"}}""");
        Check(ShipExperienceCalculator.Required(malformed.RootElement, 1, "SSR") == 0, "invalid metadata does not crash");

        var goal = new ShipGrowthGoal { ShipUid = "1", TargetLevel = 120, TargetSkillLevel = 8 };
        var ship = Ship(118, 50, 60,
            new() { Index = 1, Name = "지원 스킬", Level = 6, MaxLevel = 10 },
            new() { Index = 2, Level = 1, MaxLevel = 1 },
            new() { Index = 3, Level = 4, MaxLevel = 5 });
        var plan = ShipGrowthPlanner.Build(goal, ship, table);
        Check(plan.RemainingExperience == 250 && plan.EstimatedBattles == 5, "battle estimate rounds up");
        Check(plan.RemainingSkillLevels == 3, "skill cap and untrainable skills are respected");
        Check(!plan.IsComplete && !plan.NeedsData, "valid incomplete plan");
        Check(ShipGrowthPlanner.Build(goal, ship, table, true).NextAction.Contains("훈련 중"), "training is acknowledged");
        var noSample = ShipGrowthPlanner.Build(goal with { TrackSkills = false }, Ship(118, 0, 0), table);
        Check(noSample.EstimatedBattles is null && noSample.EstimateSummary.Contains("표본 없음"), "no invented battle estimate");
        var capped = ShipGrowthPlanner.Build(goal with { TargetLevel = 122, TrackSkills = false }, Ship(120, 900, 30), table);
        Check(!capped.IsComplete && capped.NextAction.Contains("상한"), "banked EXP does not imply level completion");
        Check(ShipGrowthPlanner.Build(goal, null, table).NeedsData, "missing ships keep their goal pending");
        Check(ShipGrowthPlanner.Build(goal, new ShipRow { Level = 125 }, table).NeedsData, "partial observations cannot complete goals");
        var missingSkill = ShipGrowthPlanner.Build(goal, Ship(125, 0, 0, new ShipSkillDetailRow { Level = 10 }), table);
        Check(!missingSkill.IsComplete && missingSkill.NeedsData, "unknown max skill level is not assumed complete");
        var skillOnly = ShipGrowthPlanner.Build(goal with { TrackLevel = false }, Ship(1, 0, 0,
            new ShipSkillDetailRow { Level = 8, MaxLevel = 10 }), default);
        Check(skillOnly.IsComplete, "skills-only goals need no EXP metadata");
        var levelOnly = ShipGrowthPlanner.Build(goal with { TrackSkills = false }, Ship(125, 0, 0), default);
        Check(levelOnly.IsComplete, "level-only completion does not require skills");
        Check(ShipGrowthPlanner.Build(goal with { TrackLevel = false, TrackSkills = false }, ship, table).NeedsData,
            "empty goals rejected");

        Check(JsonSerializer.Deserialize<AppSettings>("{}")!.GrowthGoals.Count == 0, "legacy settings default to no goals");
        var path = Path.Combine(Path.GetTempPath(), $"azurlane-settings-check-{Guid.NewGuid():N}.json");
        try
        {
            var service = new AppSettingsService(path);
            var settings = new AppSettings { GrowthGoals = [goal] };
            service.Save(settings);
            Check(new AppSettingsService(path).Load().GrowthGoals.Single() == goal, "goals survive settings round trip");
            service.Save(new AppSettings { GrowthGoals = [goal with { TargetLevel = 125 }] });
            Check(new AppSettingsService(path).Load().GrowthGoals.Single().TargetLevel == 125, "atomic replacement updates targets");
            File.Delete(path);
            service.Save(new AppSettings { GrowthGoals = [goal with { TargetLevel = 125 }] });
            Check(File.Exists(path), "deleted settings are recreated even if serialization is unchanged");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        Console.WriteLine("Growth planner, EXP and settings checks passed.");
    }

    private static ShipRow Ship(int level, int exp, int sample, params ShipSkillDetailRow[] skills) => new()
    {
        ShipUid = "1", Name = "테스트 함선", Level = level, Exp = exp, RecentBattleExpAvg = sample,
        Rarity = "SSR", HasFullState = true, SkillDetails = skills
    };
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
