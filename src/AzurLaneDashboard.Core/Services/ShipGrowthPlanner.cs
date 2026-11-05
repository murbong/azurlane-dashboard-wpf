using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>Pure recommendations from observed state; never performs game actions.</summary>
public static class ShipGrowthPlanner
{
    public static ShipGrowthPlan Build(ShipGrowthGoal goal, ShipRow? ship, JsonElement levelTable,
        bool isTraining = false)
    {
        if ((!goal.TrackLevel && !goal.TrackSkills) ||
            goal.TargetLevel is < 1 or > ShipExperienceCalculator.MaximumLevel ||
            goal.TargetSkillLevel is < 1 or > 10)
            return new() { Goal = goal, Ship = ship, NeedsData = true,
                NextAction = "목표를 수정해 주세요. 레벨은 1~125, 스킬은 1~10으로 설정합니다." };

        if (ship is null || !ship.HasFullState || ship.Level <= 0)
            return new() { Goal = goal, Ship = ship, NeedsData = true,
                NextAction = "게임에서 도크를 열고 데이터를 새로고침해 주세요. 함선 상태가 확인되면 제안을 갱신합니다." };

        var levelDone = !goal.TrackLevel || ship.Level >= goal.TargetLevel;
        var remaining = goal.TrackLevel
            ? ShipExperienceCalculator.Remaining(ship.Level, ship.Exp, goal.TargetLevel,
                level => ShipExperienceCalculator.Required(levelTable, level, ship.Rarity))
            : 0;
        int? battles = remaining is null ? null : remaining == 0 ? 0 : ship.RecentBattleExpAvg > 0
            ? (int)Math.Ceiling(remaining.Value / (double)ship.RecentBattleExpAvg) : null;

        var skills = ship.SkillDetails;
        var unknownSkills = goal.TrackSkills && (skills.Count == 0 ||
            skills.Any(skill => skill.MaxLevel is null || skill.Level <= 0));
        var pendingSkills = skills.Where(skill => skill.MaxLevel > 1 && skill.Level > 0 &&
                skill.Level < Math.Min(goal.TargetSkillLevel, skill.MaxLevel.Value))
            .OrderBy(skill => skill.Level).ThenBy(skill => skill.Index).ToArray();
        var skillGap = goal.TrackSkills
            ? pendingSkills.Sum(skill => Math.Min(goal.TargetSkillLevel, skill.MaxLevel!.Value) - skill.Level) : 0;
        var skillsDone = !goal.TrackSkills || (!unknownSkills && skillGap == 0);
        var complete = levelDone && skillsDone;
        var needsData = (goal.TrackLevel && remaining is null) || unknownSkills;
        var actions = new List<string>();

        if (goal.TrackSkills && pendingSkills.Length > 0)
        {
            var skill = pendingSkills[0];
            var name = string.IsNullOrWhiteSpace(skill.Name) ? $"스킬 {skill.Index}" : skill.Name;
            actions.Add(isTraining ? "전술 교실에서 훈련 중입니다. 완료 후 스킬 상태를 갱신하세요."
                : $"전술 교실에서 {name} Lv.{skill.Level} → {Math.Min(goal.TargetSkillLevel, skill.MaxLevel!.Value)} 훈련을 우선 검토하세요.");
        }
        if (!levelDone)
        {
            if (remaining == 0)
                actions.Add("경험치는 충분합니다. 게임에서 한계돌파·인지각성 등 레벨 상한 조건을 확인하세요.");
            else if (remaining is null)
                actions.Add("레벨 경험치 표가 부족합니다. 설정에서 메타데이터를 갱신하세요.");
            else
            {
                if (ship.EmptySlots > 0)
                    actions.Add($"출격 전에 비어 있는 장비 슬롯 {ship.EmptySlots}개를 확인하세요.");
                actions.Add(ship.RecentBattleExpAvg > 0
                    ? "최근에 안정적으로 클리어한 해역에서 육성을 이어가세요."
                    : "이 함선의 전투 결과를 수집하면 목표까지 필요한 전투 수를 계산합니다.");
            }
        }
        if (unknownSkills)
            actions.Add("스킬 상태 또는 최대 레벨 정보가 부족합니다. 도크와 메타데이터를 갱신하세요.");
        if (complete)
            actions.Add("설정한 목표를 달성했습니다. 목표를 높이거나 다른 함선을 육성하세요.");

        return new()
        {
            Goal = goal, Ship = ship, IsComplete = complete, NeedsData = needsData,
            RemainingExperience = remaining, EstimatedBattles = battles, RemainingSkillLevels = skillGap,
            LevelSummary = !goal.TrackLevel ? "레벨 목표 사용 안 함" : levelDone
                ? $"Lv.{ship.Level} / {goal.TargetLevel} · 달성"
                : $"Lv.{ship.Level} → {goal.TargetLevel} · {goal.TargetLevel - ship.Level}레벨 남음",
            SkillSummary = !goal.TrackSkills ? "스킬 목표 사용 안 함" : unknownSkills
                ? $"스킬 정보 확인 필요 · 확인된 부족 단계 {skillGap}"
                : skillsDone ? "스킬 목표 달성" : $"스킬 {pendingSkills.Length}개 · 합계 {skillGap}레벨 남음",
            EstimateSummary = !goal.TrackLevel ? "" : levelDone ? "레벨 육성 완료" : remaining is null
                ? "남은 EXP 계산 불가 · 경험치 표 부족"
                : remaining == 0 ? "남은 EXP 0 · 레벨 상한 확인 필요"
                : $"남은 EXP {remaining:N0} · " + (battles is null ? "예상 전투 수: 표본 없음" : $"약 {battles:N0}전투"),
            NextAction = string.Join("\n", actions),
            Evidence = string.Join(" · ", new[]
            {
                goal.TrackLevel && ship.RecentBattleExpAvg > 0
                    ? $"최근 관측 {ship.RecentBattleExpAvg:N0} EXP/전투 기준. 해역·MVP·컨디션에 따라 달라집니다." : "",
                goal.TrackSkills ? "스킬은 현재 레벨이 낮은 순으로 제안하며, 각 스킬의 최대 레벨을 넘기지 않습니다." : ""
            }.Where(value => value.Length > 0))
        };
    }
}
