using System.Globalization;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>
/// Builds a leveling fleet: a few Lv.120+ "drivers" carry the stage while under-levelled "leeches"
/// take the EXP, the most urgent main-fleet leech sitting in the flagship slot for 1.5x.
/// </summary>
public static class LevelingPlanBuilder
{
    public const int ReadyLevel = 120;
    public const int SlotsPerPosition = 3;
    public const int MaxDrivers = 3;
    /// <summary>A leech this many levels under the enemy no longer clears safely; the flagship sinking is a D rank.</summary>
    public const int SafeLevelGap = 10;
    /// <summary>A vanguard leech further below the enemy than this takes the hits and sinks; it waits for an easier stage.</summary>
    public const int MaxVanguardLevelGap = 30;
    /// <summary>Driverless S rank battles needed before "0 drivers" counts as proven on a stage.</summary>
    public const int ProvenBattles = 10;
    /// <summary>How many queue rows the 쫄작 tab lists next to the fleet; the growth page lists the whole pool.</summary>
    public const int QueueLength = 12;

    // Drivers fill the vanguard lead first, then a main gun, then a second vanguard.
    private static readonly string[] DriverPositions = ["Vanguard", "Main", "Vanguard"];

    /// <summary>Regal splits campaign gear at chapter 13, where mob fleets pass about Lv.120.</summary>
    public const int LateCampaignEnemyLevel = 118;

    /// <summary>
    /// The Regal gear-map usecase for farming a stage: "C-13+" from chapter 13 on, "C-12-" before it. Event
    /// chapters have no number, so their enemy level decides. Ships without these keys fall back to "C".
    /// </summary>
    public static string GearUsecase(LevelingStage stage)
    {
        if (int.TryParse(stage.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chapter) &&
            chapter is >= 101 and <= 1599)
            return chapter / 100 >= 13 ? "C-13+" : "C-12-";
        return stage.EnemyLevel >= LateCampaignEnemyLevel ? "C-13+" : "C-12-";
    }

    /// <summary>
    /// Gear goes to drivers first (they carry the stage: vanguard lead, then the main gun), then the
    /// flagship leech, then the other leeches in slot order.
    /// </summary>
    public static IReadOnlyList<LevelingSlot> GearOrder(LevelingPlan plan) =>
        plan.VanguardSlots.Concat(plan.MainSlots).OfType<LevelingSlot>().Where(slot => slot.IsDriver)
            .OrderByDescending(slot => slot.IsLead)
            .Concat(plan.MainSlots.Concat(plan.VanguardSlots).OfType<LevelingSlot>()
                .Where(slot => slot.IsLeech)
                .OrderByDescending(slot => slot.IsFlagship))
            .ToList();

    public static LevelingPlan Build(
        IReadOnlyList<LevelingShip> ships,
        LevelingStage stage,
        IReadOnlyList<LevelingBattleRecord> records,
        ShipLevelCaps caps,
        int? driverOverride = null,
        int farmFleetId = 0,
        IReadOnlySet<string>? farmFleetUids = null)
    {
        farmFleetUids ??= new HashSet<string>(StringComparer.Ordinal);
        var excludedSubmarines = ships.Count(ship => IsCandidate(ship, ignorePosition: true) && !IsSurfacePosition(ship));
        var pool = ships.Where(ship => IsCandidate(ship, ignorePosition: false))
            .Select(ship => Leech(ship, stage, caps))
            .OrderBy(leech => leech.Group)
            .ThenBy(leech => leech.Group == 3 ? -leech.Ship.BestRatingScore : 0)
            .ThenBy(leech => leech.IsWaitingForAwakening ? 1 : 0)
            .ThenBy(leech => leech.Battles ?? int.MaxValue)
            .ThenBy(leech => leech.Ship.Uid, StringComparer.Ordinal)
            .ToList();

        var placeable = pool.Where(leech => !TooWeakForVanguard(leech.Ship, stage)).ToList();
        var (autoDrivers, reason) = AutoDriverCount(placeable, stage, records);
        var drivers = Math.Clamp(driverOverride ?? autoDrivers, 0, MaxDrivers);
        var fleet = Arrange(placeable, ships, drivers, stage, farmFleetUids);

        var placed = fleet.Slots.ToDictionary(slot => slot.ShipUid, StringComparer.Ordinal);
        var queue = pool.Select((leech, index) => QueueRow(leech, index, placed, caps, stage)).ToList();
        var leeches = fleet.Slots.Where(slot => slot.IsLeech).ToList();
        var everyone = fleet.Slots.ToList();
        return new LevelingPlan
        {
            Stage = stage,
            AutoDriverCount = autoDrivers,
            DriverCount = drivers,
            DriverReasonText = reason + FlagshipNote(fleet, stage),
            MainSlots = fleet.Main,
            VanguardSlots = fleet.Vanguard,
            Queue = queue,
            PoolCount = pool.Count,
            ExcludedSubmarineCount = excludedSubmarines,
            FarmFleetId = farmFleetId,
            FarmFleetOverlap = everyone.Count(slot => farmFleetUids.Contains(slot.ShipUid)),
            TotalExpPerBattle = leeches.Sum(slot => slot.ExpPerBattle),
            TotalExpAfterBonus = leeches.Sum(slot =>
                LevelingExperience.PerBattle(stage.SRankExp, moraleBonus: false, slot.IsFlagship)),
            BonusBattlesLeft = leeches.Select(slot => LevelingExperience.BonusBattles(slot.Ship.Morale))
                .Where(battles => battles > 0).DefaultIfEmpty(0).Min(),
            BattlesUntilExhausted = everyone.Select(slot => LevelingExperience.BattlesUntilExhausted(slot.Ship.Morale))
                .DefaultIfEmpty(0).Min(),
            LowestMorale = everyone.Select(slot => slot.Ship.Morale).DefaultIfEmpty(0).Min()
        };
    }

    /// <summary>
    /// 0 when the stage's records hold enough driverless S rank battles by a fleet no stronger than the six
    /// best leeches; otherwise 1 while the leeches stay within <see cref="SafeLevelGap"/> of the enemy, else 2.
    /// </summary>
    public static (int Drivers, string Reason) AutoDriverCount(
        IReadOnlyList<LeechDraft> pool, LevelingStage stage, IReadOnlyList<LevelingBattleRecord> records)
    {
        if (pool.Count == 0)
            return (0, "Lv.120 미만 잠금 함선이 없어 쩔을 고를 수 없습니다.");

        var sixMin = Lowest(pool, drivers: 0);
        var proven = records.Count(record => record.ReadyShipCount == 0 && record.IsSRank && record.MinLevel <= sixMin);
        var fiveMin = Lowest(pool, drivers: 1);
        var gap = stage.EnemyLevel - fiveMin;
        var basis = $"쩔 최저 Lv.{fiveMin} · 적 Lv.{stage.EnemyLevel}";
        if (proven >= ProvenBattles)
            return (0, $"쩔 최저 Lv.{sixMin} · 적 Lv.{stage.EnemyLevel} → 기사 0척 권장. 기록상 기사 없이 {proven:N0}전 모두 S랭크였습니다.");
        if (gap <= SafeLevelGap)
            return (1, $"{basis} → 기사 1척 권장.");
        return (2, $"{basis} → 기사 2척 권장.");
    }

    private static int Lowest(IReadOnlyList<LeechDraft> pool, int drivers) =>
        Fill(pool, drivers).Select(leech => leech.Ship.Level).DefaultIfEmpty(0).Min();

    // The leeches that would sit beside `drivers` drivers (see DriverPositions), three slots per position.
    private static IEnumerable<LeechDraft> Fill(IEnumerable<LeechDraft> pool, int drivers)
    {
        var free = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Main"] = SlotsPerPosition - DriverPositions.Take(drivers).Count(position => position == "Main"),
            ["Vanguard"] = SlotsPerPosition - DriverPositions.Take(drivers).Count(position => position == "Vanguard")
        };
        foreach (var leech in pool)
        {
            if (free.GetValueOrDefault(leech.Ship.Position) == 0) continue;
            free[leech.Ship.Position]--;
            yield return leech;
        }
    }

    private static FleetDraft Arrange(
        IReadOnlyList<LeechDraft> pool, IReadOnlyList<LevelingShip> ships, int drivers, LevelingStage stage,
        IReadOnlySet<string> farmFleetUids)
    {
        var driverPositions = DriverPositions.Take(drivers).ToList();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var main = new List<LevelingSlot>();
        var vanguard = new List<LevelingSlot>();
        foreach (var position in driverPositions)
        {
            var driver = PickDriver(ships, position, used, farmFleetUids);
            if (driver is null) continue;
            used.Add(driver.Uid);
            var lead = position == "Vanguard" && vanguard.Count == 0;
            (position == "Main" ? main : vanguard).Add(new LevelingSlot
            {
                Ship = driver, Role = LevelingRole.Driver, IsLead = lead, TargetLevel = driver.Level
            });
        }

        var mainLeeches = new List<LeechDraft>();
        var vanguardLeeches = new List<LeechDraft>();
        foreach (var leech in pool)
        {
            var target = leech.Ship.Position == "Main" ? mainLeeches : vanguardLeeches;
            var drivenSlots = (leech.Ship.Position == "Main" ? main : vanguard).Count;
            if (target.Count + drivenSlots >= SlotsPerPosition) continue;
            target.Add(leech);
        }

        // The flagship slot goes to the most urgent main leech that can survive the stage; a main driver
        // takes it when every main leech is too far below the enemy.
        var flagship = mainLeeches.FirstOrDefault(leech => stage.EnemyLevel - leech.Ship.Level <= SafeLevelGap);
        var mainDriver = main.FirstOrDefault();
        var mainSlots = new List<LevelingSlot?>();
        if (flagship is not null)
            mainSlots.Add(LeechSlot(flagship, stage, isFlagship: true));
        else if (mainDriver is not null)
            mainSlots.Add(Flagship(mainDriver));
        else if (mainLeeches.Count > 0)
            mainSlots.Add(LeechSlot(flagship = mainLeeches[0], stage, isFlagship: true));
        mainSlots.AddRange(main.Where(slot => !ReferenceEquals(slot, mainDriver) || flagship is not null));
        mainSlots.AddRange(mainLeeches.Where(leech => !ReferenceEquals(leech, flagship))
            .Select(leech => LeechSlot(leech, stage, isFlagship: false)));

        var vanguardSlots = new List<LevelingSlot?>(vanguard);
        vanguardSlots.AddRange(vanguardLeeches.Select(leech => LeechSlot(leech, stage, isFlagship: false)));
        return new FleetDraft(Pad(mainSlots), Pad(vanguardSlots));
    }

    private static LevelingSlot Flagship(LevelingSlot driver) => new()
    {
        Ship = driver.Ship, Role = driver.Role, IsFlagship = true, IsLead = driver.IsLead, TargetLevel = driver.TargetLevel
    };

    private static IReadOnlyList<LevelingSlot?> Pad(List<LevelingSlot?> slots)
    {
        while (slots.Count < SlotsPerPosition) slots.Add(null);
        return slots.Take(SlotsPerPosition).ToList();
    }

    private static LevelingShip? PickDriver(
        IReadOnlyList<LevelingShip> ships, string position, IReadOnlySet<string> used, IReadOnlySet<string> farmFleetUids)
    {
        return ships
            .Where(ship => ship.Level >= ReadyLevel && !ship.IsBulin && ship.Position == position && !used.Contains(ship.Uid))
            .OrderByDescending(ship => farmFleetUids.Contains(ship.Uid))
            .ThenByDescending(ship => ship.MobRatingScore)
            .ThenByDescending(ship => position == "Vanguard" ? ship.SelfSurvival : ship.AoeDamage)
            .ThenByDescending(ship => ship.Level)
            .ThenBy(ship => ship.Uid, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static LevelingSlot LeechSlot(LeechDraft leech, LevelingStage stage, bool isFlagship) => new()
    {
        Ship = leech.Ship,
        Role = LevelingRole.Leech,
        IsFlagship = isFlagship,
        TargetLevel = leech.TargetLevel,
        ExpPerBattle = LevelingExperience.PerBattle(stage.SRankExp,
            leech.Ship.Morale > LevelingExperience.MoraleBonusAbove, isFlagship)
    };

    private static string FlagshipNote(FleetDraft fleet, LevelingStage stage)
    {
        var weak = fleet.Slots.Where(slot => slot.IsLeech && stage.EnemyLevel - slot.Ship.Level > SafeLevelGap)
            .Select(slot => slot.Ship.Level).ToList();
        var flagship = fleet.Main.FirstOrDefault();
        if (flagship is { IsLeech: true } && stage.EnemyLevel - flagship.Ship.Level > SafeLevelGap)
            return $" 기함 Lv.{flagship.Ship.Level}이 적보다 많이 낮아 격침되면 D랭크입니다. 기사를 늘리세요.";
        return weak.Count > 0 ? $" Lv.{weak.Min()} 쩔은 기함에 두지 않았습니다." : "";
    }

    private static LevelingQueueRow QueueRow(
        LeechDraft leech, int index, IReadOnlyDictionary<string, LevelingSlot> placed, ShipLevelCaps caps,
        LevelingStage stage)
    {
        var ship = leech.Ship;
        var slot = placed.GetValueOrDefault(ship.Uid);
        var tooWeak = TooWeakForVanguard(ship, stage);
        var status = slot is not null
            ? $"편성 · {(slot.IsFlagship ? "기함" : ship.Position == "Main" ? "주력" : "전위")}"
            : tooWeak ? "이 해역 제외"
            : ship.FarmFleetId > 0 ? "다음 교대" : "대기";
        var statusKind = slot is not null ? "in" : tooWeak ? "out" : ship.FarmFleetId > 0 ? "next" : "wait";
        var tags = new List<LevelingTag>();
        if (ship.IsGrowthGoal) tags.Add(new LevelingTag("goal", "육성 목표"));
        if (ship.IsMetaGuideShip) tags.Add(new LevelingTag("content", "META 공략"));
        if (ship.ContentReach > 0)
            tags.Add(ship.ContentLabels.Count == 1
                ? new LevelingTag("content", $"{ship.ContentLabels[0]} 후보")
                : new LevelingTag("content", $"{ship.ContentReach}개 콘텐츠 후보", string.Join(" · ", ship.ContentLabels)));
        if (ship.FarmFleetId > 0) tags.Add(new LevelingTag("farm", $"게임 {ship.FarmFleetId}함대 쫄작 중"));
        if (ship.TemplateMaxLevel is > 0 and < 100)
            tags.Add(new LevelingTag("warn", $"한계돌파 미완료 · Lv.{ship.TemplateMaxLevel} 상한"));
        var cost = caps.Awakening(leech.TargetLevel, ship.Rarity);
        var stepNote = ship.TemplateMaxLevel is > 0 and < 100 && leech.TargetLevel >= ship.TemplateMaxLevel
            ? "한계돌파 전 상한"
            : "";
        if (cost is not null && leech.TargetLevel < ShipExperienceCalculator.MaximumLevel)
        {
            stepNote = string.Create(CultureInfo.CurrentCulture, $"각성 유닛 {cost.Units:N0}");
            tags.Add(new LevelingTag("info", string.Create(CultureInfo.CurrentCulture,
                    $"Lv.{leech.TargetLevel} 각성 · 유닛 {cost.Units:N0}"),
                string.Create(CultureInfo.CurrentCulture,
                    $"인식 각성 비용: 금화 {cost.Gold:N0} · 성정 유닛 {cost.Units:N0}") +
                (cost.UnitsII > 0
                    ? string.Create(CultureInfo.CurrentCulture, $" · 성정 유닛II {cost.UnitsII:N0}")
                    : "")));
        }

        var gap = stage.EnemyLevel - ship.Level;
        if (tooWeak)
            tags.Add(new LevelingTag("warn", $"적보다 Lv.{gap} 낮음",
                "전위로 넣으면 격침되기 쉬워 이 해역에서는 대기열에만 둡니다. 더 쉬운 해역에서 키우세요."));
        else if (gap > SafeLevelGap)
            tags.Add(new LevelingTag("warn", slot is null
                ? $"적보다 Lv.{gap} 낮음 · 넣으면 기사 2척 권장"
                : $"적보다 Lv.{gap} 낮아 기함 제외"));

        return new LevelingQueueRow
        {
            Rank = index + 1,
            Ship = ship,
            Group = leech.Group,
            StatusText = status,
            StatusKind = statusKind,
            IsInFleet = slot is not null,
            IsFlagship = slot?.IsFlagship == true,
            StepNote = stepNote,
            StageLabel = stage.Label,
            TargetLevel = leech.TargetLevel,
            ExperienceNeeded = leech.Needed,
            Battles = slot?.IsFlagship == true
                ? LevelingExperience.BattlesFor(leech.Needed, stage.SRankExp, ship.Morale, flagship: true)
                : leech.Battles,
            IsWaitingForAwakening = leech.IsWaitingForAwakening,
            Tags = tags
        };
    }

    // A ship stuck at its limit-break cap gains nothing until it is limit broken.
    private static bool IsCandidate(LevelingShip ship, bool ignorePosition) =>
        ship.IsLocked && !ship.IsBulin && ship.Level is > 0 and < ReadyLevel &&
        !(ship.TemplateMaxLevel is > 0 and < 100 && ship.Level >= ship.TemplateMaxLevel) &&
        (ignorePosition || IsSurfacePosition(ship));

    private static bool TooWeakForVanguard(LevelingShip ship, LevelingStage stage) =>
        ship.Position == "Vanguard" && stage.EnemyLevel - ship.Level > MaxVanguardLevelGap;

    // Submarines only earn EXP in battles where their support strike fires, so they are not farmed this way.
    private static bool IsSurfacePosition(LevelingShip ship) => ship.Position is "Main" or "Vanguard";

    private static LeechDraft Leech(LevelingShip ship, LevelingStage stage, ShipLevelCaps caps)
    {
        var target = caps.NextStop(ship.Level, ship.Experience, ship.Rarity, ship.TemplateMaxLevel);
        var waiting = caps.IsWaitingForAwakening(ship.Level, ship.Experience, ship.Rarity);
        var needed = waiting
            ? 0
            : ShipExperienceCalculator.Remaining(ship.Level, ship.Experience, target,
                level => caps.Required(level, ship.Rarity)) ?? 0;
        var group = ship.IsGrowthGoal ? 0 : ship.ContentReach > 0 || ship.IsMetaGuideShip ? 1 : ship.FarmFleetId > 0 ? 2 : 3;
        return new LeechDraft(ship, group, target, needed, waiting,
            waiting ? 0 : LevelingExperience.BattlesFor(needed, stage.SRankExp, ship.Morale));
    }

    public sealed record LeechDraft(
        LevelingShip Ship, int Group, int TargetLevel, long Needed, bool IsWaitingForAwakening, int? Battles);

    private sealed record FleetDraft(IReadOnlyList<LevelingSlot?> Main, IReadOnlyList<LevelingSlot?> Vanguard)
    {
        public IEnumerable<LevelingSlot> Slots => Main.Concat(Vanguard).OfType<LevelingSlot>();
    }
}
