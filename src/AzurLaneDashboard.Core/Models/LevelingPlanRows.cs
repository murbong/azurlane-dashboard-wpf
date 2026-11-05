using System.Globalization;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.Core.Models;

/// <summary>A ship as the leveling planner sees it; drivers and leeches come from the same list.</summary>
public sealed record LevelingShip
{
    public string Uid { get; init; } = "";
    public string Name { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string Hull { get; init; } = "";
    /// <summary>Community fleet position: "Main", "Vanguard" or "Submarine".</summary>
    public string Position { get; init; } = "";
    public int Level { get; init; }
    public int Experience { get; init; }
    /// <summary>EXP the current level needs; 0 at Lv.125 or without level data.</summary>
    public int ExperienceRequired { get; init; }
    /// <summary>EXP inside the current level as a percentage of <see cref="ExperienceRequired"/>, 100 when full.</summary>
    public double ExperiencePercent { get; init; }
    /// <summary>EXP earned since Lv.1, so a gain across a level-up still reads as one number; 0 when unknown.</summary>
    public long TotalExperience { get; init; }
    public int Morale { get; init; }
    public bool IsLocked { get; init; }
    public bool IsBulin { get; init; }
    /// <summary>The template's max level: 70/80/90 until the ship is fully limit broken.</summary>
    public int TemplateMaxLevel { get; init; } = 100;
    public bool IsGrowthGoal { get; init; }
    /// <summary>How many planner contents list the ship as a candidate.</summary>
    public int ContentReach { get; init; }
    /// <summary>Labels of the planner contents that list the ship ("14 해역", "EX 랭킹").</summary>
    public IReadOnlyList<string> ContentLabels { get; init; } = [];
    public bool IsMetaGuideShip { get; init; }
    /// <summary>Game fleet id when the ship sits in the fleet detected as the farming fleet, else 0.</summary>
    public int FarmFleetId { get; init; }
    /// <summary>Best community rating score (FleetPlanRating) across contents.</summary>
    public int BestRatingScore { get; init; }
    /// <summary>Community chapter mob rating score, used to pick drivers.</summary>
    public int MobRatingScore { get; init; }
    public double SelfSurvival { get; init; }
    public double AoeDamage { get; init; }

    public string ExperienceSummary => ExperienceRequired > 0
        ? string.Create(CultureInfo.CurrentCulture,
            $"Lv.{Level} EXP {Experience:N0} / {ExperienceRequired:N0} ({ExperiencePercent:0.#}%)")
        : string.Create(CultureInfo.CurrentCulture, $"Lv.{Level} EXP {Experience:N0}");
}

/// <summary>
/// A ship's EXP as the animated views read it: bars fill inside the level (or toward Lv.120) and the "+EXP"
/// floater shows how far <see cref="Total"/> grew since the view last showed the ship.
/// </summary>
public sealed record ExpGauge(string Uid, int Level, long Total, double LevelFraction)
{
    public static ExpGauge Of(LevelingShip ship) =>
        new(ship.Uid, ship.Level, ship.TotalExperience, Math.Clamp(ship.ExperiencePercent / 100, 0, 1));

    /// <summary>Progress toward Lv.120 counting the EXP inside the level, so it moves with every battle.</summary>
    public double ReadyFraction => Math.Clamp((Level + LevelFraction) / LevelingPlanBuilder.ReadyLevel, 0, 1);
}

/// <summary>A stage to farm, with the per-ship S rank EXP either observed or derived from Lua.</summary>
public sealed record LevelingStage
{
    public string Key { get; init; } = "";
    /// <summary>Short chapter name ("D3", "12-4").</summary>
    public string Label { get; init; } = "";
    /// <summary>Chapter name with its title ("D3 껍질을 깨부술 때").</summary>
    public string Title { get; init; } = "";
    public string Note { get; init; } = "";
    public int EnemyLevel { get; init; }
    public int LuaExp { get; init; }
    /// <summary>Most common S rank EXP seen without morale bonus or multipliers; 0 when there is no record.</summary>
    public int ObservedSRankExp { get; init; }
    public int RecordBattles { get; init; }
    public int RecordSRankBattles { get; init; }

    public int SRankExp => ObservedSRankExp > 0 ? ObservedSRankExp : LevelingExperience.SRank(LuaExp);
    public int BonusExp => LevelingExperience.PerBattle(SRankExp, moraleBonus: true);
    public bool HasRecords => RecordBattles > 0;
    public string BasisText => HasRecords ? $"기록 {RecordBattles:N0}전" : "Lua 기준";
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Label : Title;
}

/// <summary>One captured battle on the selected stage, reduced to what the driver rule needs.</summary>
public sealed record LevelingBattleRecord(int MinLevel, int ReadyShipCount, bool IsSRank);

public enum LevelingRole
{
    Driver,
    Leech
}

public sealed class LevelingSlot
{
    public LevelingShip Ship { get; init; } = new();
    public LevelingRole Role { get; init; }
    public bool IsFlagship { get; init; }
    public bool IsLead { get; init; }
    public int TargetLevel { get; init; }
    public int ExpPerBattle { get; init; }

    public string ShipUid => Ship.Uid;
    public string Name => Ship.Name;
    public string IconUrl => Ship.IconUrl;
    public string Rarity => Ship.Rarity;
    public bool IsDriver => Role == LevelingRole.Driver;
    public bool IsLeech => Role == LevelingRole.Leech;
    public string RoleText => IsDriver ? "기사" : "쩔";
    public string SubText => IsDriver
        ? $"Lv.{Ship.Level} · {(Ship.FarmFleetId > 0 ? $"{Ship.FarmFleetId}함대" : FleetPlanBuilder.HullLabel(Ship.Hull))}"
        : $"Lv.{Ship.Level} → {TargetLevel}";
    public string ExpText => IsDriver
        ? $"기사 · {(IsLead ? "선두" : "화력")}"
        : string.Create(CultureInfo.CurrentCulture, $"+{ExpPerBattle:N0} /전");
    public ExpGauge Gauge => ExpGauge.Of(Ship);
    /// <summary>Gear slots the recommendation would change on this ship; 0 when none or not computed.</summary>
    public int GearChanges { get; init; }
    public bool HasGearChanges => GearChanges > 0;
    public string GearBadgeText => $"장비 {GearChanges}";
    public string GearBadgeToolTip => $"바꿀 장비 칸 {GearChanges}개 · 눌러서 장비 추천 보기";

    public LevelingSlot WithGearChanges(int changes) => new()
    {
        Ship = Ship, Role = Role, IsFlagship = IsFlagship, IsLead = IsLead, TargetLevel = TargetLevel,
        ExpPerBattle = ExpPerBattle, GearChanges = changes
    };
}

public sealed record LevelingTag(string Kind, string Text, string ToolTip = "")
{
    public bool HasToolTip => ToolTip.Length > 0;
}

public sealed class LevelingQueueRow
{
    public int Rank { get; init; }
    public LevelingShip Ship { get; init; } = new();
    /// <summary>Why the ship sits where it does: 0 growth goal, 1 content or META candidate, 2 farming now, 3 the rest.</summary>
    public int Group { get; init; }
    /// <summary>"편성 · 기함/주력/전위", "다음 교대", "대기" or "이 해역 제외".</summary>
    public string StatusText { get; init; } = "";
    /// <summary>"in", "next", "wait" or "out", for the status chip colour.</summary>
    public string StatusKind { get; init; } = "wait";
    public bool IsInFleet { get; init; }
    public bool IsFlagship { get; init; }
    /// <summary>"각성 유닛 300" at an awakening stop, "한계돌파 전 상한" below a limit-break cap, else empty.</summary>
    public string StepNote { get; init; } = "";
    /// <summary>The stage the battle counts are for ("D3").</summary>
    public string StageLabel { get; init; } = "";
    public int TargetLevel { get; init; }
    public long ExperienceNeeded { get; init; }
    public int? Battles { get; init; }
    public bool IsWaitingForAwakening { get; init; }
    public IReadOnlyList<LevelingTag> Tags { get; init; } = [];

    public string ShipUid => Ship.Uid;
    public string Name => Ship.Name;
    public string IconUrl => Ship.IconUrl;
    public string Rarity => Ship.Rarity;
    public bool IsGoal => Ship.IsGrowthGoal;
    public string HeaderText => $"{FleetPlanBuilder.HullLabel(Ship.Hull)} · Lv.{Ship.Level}";
    public bool HasTags => Tags.Count > 0;
    /// <summary>The reasons and warnings; the awakening cost is shown with the next step instead.</summary>
    public IReadOnlyList<LevelingTag> ReasonTags => Tags.Where(tag => tag.Kind != "info").ToList();
    public string GroupTitle => Group switch
    {
        0 => "① 육성 목표",
        1 => "② 콘텐츠·META 후보",
        2 => "③ 지금 쫄작 중",
        _ => "④ 나머지"
    };
    public string GroupNote => Group switch
    {
        0 => "직접 고정한 함선 · 다음 단계까지 가까운 순",
        1 => "추천 편성이나 META 공략에 들어갈 함선 · 가까운 순",
        2 => Ship.FarmFleetId > 0 ? $"게임 {Ship.FarmFleetId}함대에서 키우는 중 · 가까운 순" : "쫄작 함대에서 키우는 중",
        _ => "커뮤니티 평가가 높은 순"
    };
    public string StepText => IsWaitingForAwakening ? $"Lv.{TargetLevel} 각성 대기" : $"Lv.{Ship.Level} → {TargetLevel}";
    public string ExperienceText => IsWaitingForAwakening
        ? "-"
        : string.Create(CultureInfo.CurrentCulture, $"{ExperienceNeeded:N0}");
    public string BattlesText => IsWaitingForAwakening ? "각성 필요"
        : Battles is { } battles ? string.Create(CultureInfo.CurrentCulture, $"약 {battles:N0}전") : "-";
    public string BattlesNote => IsFlagship ? "기함 ×1.5" : "";
    public string RankText => $"대기열 {Rank}위";
    /// <summary>The next step with its battle count on the queue's stage, for cards outside the queue.</summary>
    public string PlanText => IsWaitingForAwakening
        ? $"Lv.{TargetLevel}에서 인식 각성 대기"
        : Battles is { } battles
            ? string.Create(CultureInfo.CurrentCulture, $"{StageLabel}에서 Lv.{TargetLevel}까지 약 {battles:N0}전")
            : $"Lv.{TargetLevel}까지";
    public IReadOnlyList<LevelingTag> WarningTags => Tags.Where(tag => tag.Kind == "warn").ToList();
    public bool HasWarning => WarningTags.Count > 0;
    public ExpGauge Gauge => ExpGauge.Of(Ship);
    /// <summary>Lv.120 progress including the EXP inside the level.</summary>
    public double Progress => Gauge.ReadyFraction;
    public string GoalText => IsWaitingForAwakening
        ? $"Lv.{TargetLevel}에서 인식 각성 대기"
        : string.Create(CultureInfo.CurrentCulture,
            $"Lv.{TargetLevel}까지 {ExperienceNeeded:N0} EXP · {(Battles is { } battles ? $"약 {battles:N0}전" : "전투 수 계산 불가")}");
    public string CompactText => IsWaitingForAwakening
        ? $"{FleetPlanBuilder.HullLabel(Ship.Hull)} · Lv.{Ship.Level} 각성 대기"
        : string.Create(CultureInfo.CurrentCulture,
            $"{FleetPlanBuilder.HullLabel(Ship.Hull)} · Lv.{Ship.Level} → {TargetLevel} · 약 {Battles ?? 0:N0}전");
}

public sealed class LevelingPlan
{
    public LevelingStage Stage { get; init; } = new();
    public int AutoDriverCount { get; init; }
    public int DriverCount { get; init; }
    public bool IsDriverCountManual => DriverCount != AutoDriverCount;
    public string DriverReasonText { get; init; } = "";
    public IReadOnlyList<LevelingSlot?> MainSlots { get; init; } = [];
    public IReadOnlyList<LevelingSlot?> VanguardSlots { get; init; } = [];
    public IReadOnlyList<LevelingQueueRow> Queue { get; init; } = [];
    public int PoolCount { get; init; }
    public int ExcludedSubmarineCount { get; init; }
    public int FarmFleetId { get; init; }
    public int FarmFleetOverlap { get; init; }
    public int TotalExpPerBattle { get; init; }
    public int TotalExpAfterBonus { get; init; }
    public int BonusBattlesLeft { get; init; }
    public int BattlesUntilExhausted { get; init; }
    public int LowestMorale { get; init; }

    public IEnumerable<LevelingSlot> Slots => MainSlots.Concat(VanguardSlots).OfType<LevelingSlot>();
    public int LeechCount => Slots.Count(slot => slot.IsLeech);
    public bool HasShips => Slots.Any();
    public string FleetSummaryText => FarmFleetId > 0
        ? $"{FarmFleetId}함대에 편성 · 기사 {DriverCount} · 쩔 {LeechCount}"
        : $"기사 {DriverCount} · 쩔 {LeechCount}";
    public string OverlapText => FarmFleetId == 0
        ? "쫄작 함대 기록 없음"
        : FarmFleetOverlap >= 6 ? $"게임 {FarmFleetId}함대와 같음" : $"게임 {FarmFleetId}함대와 {FarmFleetOverlap}/6척 같음";
    public bool HasRecordChip => Stage.RecordBattles > 0;
    public bool IsRecordAllSRank => Stage.RecordBattles > 0 && Stage.RecordSRankBattles == Stage.RecordBattles;
    public string TotalExpText => string.Create(CultureInfo.CurrentCulture, $"{TotalExpPerBattle:N0}");
    public string TotalAfterBonusText => BonusBattlesLeft > 0
        ? string.Create(CultureInfo.CurrentCulture, $"보너스 끝나면 {TotalExpAfterBonus:N0}")
        : "전의 보너스 없음";
    public string BonusBattlesText => BonusBattlesLeft > 0 ? $"{BonusBattlesLeft}전 남음" : "없음";
    public string ExhaustText => $"{BattlesUntilExhausted}전";
    public string LowestMoraleText => $"최저 전의 {LowestMorale}";
    public string LeechCountText => $"쩔 {LeechCount}척 전투당 EXP 합계";
    public string RecordChipText => Stage.RecordBattles == 0
        ? ""
        : Stage.RecordSRankBattles == Stage.RecordBattles
            ? $"✓ 최근 {Stage.Label} {Stage.RecordBattles:N0}전 모두 S랭크"
            : $"S랭크 {Stage.RecordSRankBattles:N0}/{Stage.RecordBattles:N0}";
}
