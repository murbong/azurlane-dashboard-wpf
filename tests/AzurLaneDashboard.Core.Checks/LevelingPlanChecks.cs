using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

internal static class LevelingPlanChecks
{
    public static void Run()
    {
        CheckExperience();
        var caps = new ShipLevelCaps(LevelTable());
        CheckCaps(caps);
        CheckProvenStage(caps);
        CheckSingleDriver(caps);
        CheckWeakLeech(caps);
        CheckQueue(caps);
        CheckLiveState(caps);
        CheckGear(caps);
        CheckSeedMap();
        Console.WriteLine("Leveling plan, EXP and level cap checks passed.");
    }

    private static void CheckExperience()
    {
        // Captured D3 battles: Lua exp 702 → 842 (S), 1,010 (morale > 120), 1,515 (flagship), 2,020 (MVP).
        var s = LevelingExperience.SRank(702);
        Check(s == 842, "S rank pays floor(exp x 1.2)");
        Check(LevelingExperience.PerBattle(s, moraleBonus: true) == 1010, "morale above 120 adds x1.2");
        Check(LevelingExperience.PerBattle(s, moraleBonus: true, flagship: true) == 1515, "flagship x1.5 rounds down after morale");
        Check(LevelingExperience.PerBattle(s, moraleBonus: true, mvp: true) == 2020, "MVP doubles");
        Check(LevelingExperience.SRank(761) == 913 && LevelingExperience.PerBattle(913, true) == 1095, "boss battle matches too");
        Check(LevelingExperience.BonusBattles(134) == 7 && LevelingExperience.BonusBattles(150) == 15 &&
              LevelingExperience.BonusBattles(121) == 1 && LevelingExperience.BonusBattles(120) == 0,
            "bonus battles last while morale stays above 120");
        Check(LevelingExperience.BattlesUntilExhausted(119) == 59, "two morale per battle");
        // 7 bonus battles at 1,515 then 1,263 each: 10,605 + 28 x 1,263 covers 44,744.
        Check(LevelingExperience.BattlesFor(44_744, s, 134, flagship: true) == 35, "battle count spends bonus battles first");
        Check(LevelingExperience.BattlesFor(0, s, 134) == 0 && LevelingExperience.BattlesFor(10, 0, 134) is null,
            "no invented battle counts");
    }

    private static void CheckCaps(ShipLevelCaps caps)
    {
        Check(caps.Caps.SequenceEqual([100, 105]), "caps are the level_limit rows");
        Check(caps.NextStop(103, 0, "UR", 100) == 105, "next cap above the level");
        Check(caps.NextStop(71, 0, "SSR", 90) == 90, "a ship short of full limit break stops at its template cap");
        Check(caps.IsWaitingForAwakening(105, 5000, "UR") && caps.NextStop(105, 5000, "UR", 100) == 105,
            "banked EXP on a cap means it waits for awakening");
        Check(!caps.IsWaitingForAwakening(105, 10, "UR") && caps.NextStop(105, 10, "UR", 100) == 125,
            "without banked EXP the ship is past that cap");
        Check(caps.Awakening(105, "DR") == new AwakeningCost(6000, 600, 0), "awakening cost by rarity code");
        Check(caps.Awakening(105, "SSR") == new AwakeningCost(4000, 400, 0), "SSR uses rarity code 5");
        Check(caps.Awakening(104, "UR") is null, "no cost off a cap");

        // Battle EXP follows the client's Ship.addExp: level up until a cap, then bank there.
        Check(caps.AddExperience(98, 500, 1700, "SSR", 100) == (100, 200), "levels up to the cap and keeps the rest");
        Check(caps.AddExperience(100, 200, 5000, "SSR", 100) == (100, 5200), "a ship on a cap banks the EXP");
        Check(caps.AddExperience(69, 900, 5000, "SSR", 70) == (70, 4900), "a ship short of limit break stops at its template cap");
        Check(caps.AddExperience(101, 0, 2500, "UR", 100) == (103, 100), "UR ships use the UR table past a cap");
        Check(caps.AddExperience(100, ShipLevelCaps.OverflowMax - 10, 50, "SSR", 100) == (100, ShipLevelCaps.OverflowMax),
            "banked EXP stops at the overflow cap");
        Check(caps.Total(1, 300, "SSR") == 300 && caps.Total(3, 10, "SSR") == 2010 && caps.Total(3, 10, "UR") == 2410,
            "total EXP adds every level below");
        var gauge = new ExpGauge("a", 60, 0, 0.5);
        Check(Math.Abs(gauge.ReadyFraction - 60.5 / 120) < 1e-9 && new ExpGauge("b", 125, 0, 1).ReadyFraction == 1,
            "Lv.120 progress counts the EXP inside the level");
    }

    private static void CheckProvenStage(ShipLevelCaps caps)
    {
        var ships = Leeches().ToList();
        var stage = D3() with { RecordBattles = 12, RecordSRankBattles = 12 };
        var records = Enumerable.Repeat(new LevelingBattleRecord(99, 0, true), 12).ToList();
        var plan = LevelingPlanBuilder.Build(ships, stage, records, caps);
        Check(plan.AutoDriverCount == 0 && plan.DriverCount == 0, "driverless S rank records prove 0 drivers");
        Check(plan.LeechCount == 6 && plan.DriverReasonText.Contains("12전 모두 S랭크"), "six leeches and the proof line");
        Check(plan.RecordChipText == "✓ 최근 D3 12전 모두 S랭크", "record chip");

        var weakRecords = Enumerable.Repeat(new LevelingBattleRecord(110, 0, true), 12).ToList();
        Check(LevelingPlanBuilder.Build(ships, stage, weakRecords, caps).AutoDriverCount == 1,
            "records by a stronger fleet prove nothing for weaker leeches");
        var overridden = LevelingPlanBuilder.Build(ships.Concat(Drivers()).ToList(), stage, records, caps, driverOverride: 3);
        Check(overridden.DriverCount == 3 && overridden.IsDriverCountManual && overridden.LeechCount == 3,
            "a manual driver count replaces the automatic one");
    }

    private static void CheckSingleDriver(ShipLevelCaps caps)
    {
        var farm = new HashSet<string>(StringComparer.Ordinal) { "lead-farm", "v-goal" };
        var ships = Leeches().Concat(Drivers()).ToList();
        var plan = LevelingPlanBuilder.Build(ships, D3(), [], caps, farmFleetId: 1, farmFleetUids: farm);
        Check(plan.AutoDriverCount == 1, "leeches within ten levels of the enemy need one driver");
        var lead = plan.VanguardSlots[0];
        Check(lead is { IsDriver: true, IsLead: true } && lead.ShipUid == "lead-farm",
            "the vanguard lead driver comes from the farming fleet first");
        Check(plan.MainSlots.All(slot => slot is { IsLeech: true }) && plan.VanguardSlots.Count(slot => slot?.IsLeech == true) == 2,
            "one driver leaves three main and two vanguard leeches");
        var flagship = plan.MainSlots[0]!;
        Check(flagship.IsFlagship && flagship.ShipUid == "m-goal" && flagship.ExpPerBattle == 1515,
            "the most urgent main leech is flagship and earns 1,515 with morale bonus");
        Check(plan.MainSlots[1]!.ExpPerBattle == 842, "a leech at 110 morale earns the plain 842");
        Check(plan.TotalExpPerBattle == plan.Slots.Where(slot => slot.IsLeech).Sum(slot => slot.ExpPerBattle),
            "total is the leech sum");
        Check(plan.FarmFleetOverlap == 2 && plan.OverlapText == "게임 1함대와 2/6척 같음", "overlap with the farming fleet");
        Check(plan.BattlesUntilExhausted == 55 && plan.LowestMorale == 110, "fleet runs dry at its lowest morale");
    }

    private static void CheckWeakLeech(ShipLevelCaps caps)
    {
        var rookie = Ship("rookie", "Main", 1, morale: 150) with { IsGrowthGoal = true };
        var ships = Leeches().Prepend(rookie).Concat(Drivers()).ToList();
        var plan = LevelingPlanBuilder.Build(ships, D3(), [], caps);
        Check(plan.AutoDriverCount == 2, "a leech far below the enemy calls for two drivers");
        Check(plan.MainSlots[0] is { IsFlagship: true } flagship && flagship.ShipUid != "rookie",
            "a leech far below the enemy never sits in the flagship slot");
        Check(plan.MainSlots[1] is { IsDriver: true }, "the main driver takes the second main slot");
        Check(plan.Slots.Any(slot => slot.ShipUid == "rookie") && plan.DriverReasonText.Contains("기함에 두지 않았습니다"),
            "the weak leech still farms, off the flagship");
        var row = plan.Queue.Single(item => item.ShipUid == "rookie");
        Check(row.Tags.Any(tag => tag.Kind == "warn" && tag.Text == "적보다 Lv.103 낮아 기함 제외"),
            "queue says why the weak leech is off the flagship");

        // A vanguard that far below the enemy would take the hits; it waits instead of forcing more drivers.
        var weakVanguard = Ship("weak-vanguard", "Vanguard", 1) with { IsGrowthGoal = true };
        var withVanguard = LevelingPlanBuilder.Build(Leeches().Prepend(weakVanguard).Concat(Drivers()).ToList(), D3(), [], caps);
        Check(withVanguard.AutoDriverCount == 1 && withVanguard.Slots.All(slot => slot.ShipUid != "weak-vanguard"),
            "a vanguard leech far below the enemy stays out of the fleet and the driver rule");
        var waiting = withVanguard.Queue.Single(item => item.ShipUid == "weak-vanguard");
        Check(waiting.StatusText == "이 해역 제외" && waiting.Tags.Any(tag => tag.Text == "적보다 Lv.103 낮음" && tag.HasToolTip),
            "the queue says why it waits");
    }

    private static void CheckQueue(ShipLevelCaps caps)
    {
        var ships = new List<LevelingShip>
        {
            Ship("other-strong", "Main", 100) with { BestRatingScore = 52 },
            Ship("other-weak", "Main", 100) with { BestRatingScore = 22 },
            Ship("farm", "Vanguard", 110) with { FarmFleetId = 1 },
            Ship("content", "Main", 103) with { ContentReach = 3 },
            Ship("goal", "Vanguard", 112) with { IsGrowthGoal = true, IsMetaGuideShip = true },
            Ship("capped", "Main", 105, experience: 5000) with { ContentReach = 1 },
            Ship("broken", "Vanguard", 71, rarity: "SSR") with { TemplateMaxLevel = 90 },
            Ship("sub", "Submarine", 100),
            Ship("unlocked", "Main", 100) with { IsLocked = false },
            Ship("ready", "Main", 120),
            Ship("stuck", "Main", 70, rarity: "SR") with { TemplateMaxLevel = 70 }
        };
        var plan = LevelingPlanBuilder.Build(ships, D3(), [], caps);
        Check(plan.ExcludedSubmarineCount == 1 && plan.PoolCount == 7,
            "submarines, unlocked, ready and limit-break-capped ships are not leeches");
        var order = plan.Queue.Select(row => row.ShipUid).ToList();
        Check(order.Take(3).SequenceEqual(["goal", "content", "capped"]), "growth goals, then content candidates; awakening waits last");
        Check(order.IndexOf("farm") < order.IndexOf("other-strong") && order.IndexOf("other-strong") < order.IndexOf("other-weak"),
            "farming fleet ships, then the rest by rating");
        var goal = plan.Queue[0];
        Check(goal.Tags.Select(tag => tag.Text).Take(2).SequenceEqual(["육성 목표", "META 공략"]), "goal row tags");
        var capped = plan.Queue.Single(row => row.ShipUid == "capped");
        Check(capped.IsWaitingForAwakening && capped.ExperienceNeeded == 0 && capped.GoalText.Contains("인식 각성 대기"),
            "a capped ship waits for awakening instead of farming");
        var broken = plan.Queue.Single(row => row.ShipUid == "broken");
        Check(broken.TargetLevel == 90 && broken.Tags.Any(tag => tag.Text == "한계돌파 미완료 · Lv.90 상한"),
            "limit break cap shows on the row");
        Check(plan.Queue.Single(row => row.ShipUid == "content").Tags.Any(tag => tag.Text.StartsWith("Lv.105 각성")),
            "rows name the next awakening cost");
    }

    private static void CheckGear(ShipLevelCaps caps)
    {
        Check(LevelingPlanBuilder.GearUsecase(new LevelingStage { Key = "1204", EnemyLevel = 113 }) == "C-12-" &&
              LevelingPlanBuilder.GearUsecase(new LevelingStage { Key = "1304", EnemyLevel = 121 }) == "C-13+",
            "standard chapters pick the campaign gear ranking by chapter number");
        Check(LevelingPlanBuilder.GearUsecase(new LevelingStage { Key = "2060026", EnemyLevel = 104 }) == "C-12-" &&
              LevelingPlanBuilder.GearUsecase(new LevelingStage { Key = "2060099", EnemyLevel = 121 }) == "C-13+",
            "event chapters pick it by enemy level");

        // Two drivers: gear goes to the vanguard lead, then the main gun, then the flagship leech and the rest.
        var plan = LevelingPlanBuilder.Build(Leeches().Concat(Drivers()).ToList(), D3(), [], caps, driverOverride: 2);
        var order = LevelingPlanBuilder.GearOrder(plan);
        Check(order.Count == plan.Slots.Count() && order[0] is { IsDriver: true, IsLead: true } &&
              order[1] is { IsDriver: true, IsLead: false } && order[2].IsFlagship &&
              order.Skip(3).All(slot => slot.IsLeech && !slot.IsFlagship),
            "drivers get gear first, then the flagship leech");
        var badge = order[0].WithGearChanges(3);
        Check(badge.HasGearChanges && badge.GearBadgeText == "장비 3" && badge.ShipUid == order[0].ShipUid &&
              !order[0].HasGearChanges, "a tile copy carries the gear change count");
    }

    private static void CheckSeedMap()
    {
        var path = FindUp(Path.Combine("maps", "ship_level.json"));
        if (path is null) return;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var caps = new ShipLevelCaps(document.RootElement.Clone());
        Check(caps.Caps.SequenceEqual([100, 105, 110, 115, 120, 125]), "seed ship_level.json lists the six caps");
        Check(caps.Awakening(105, "UR") == new AwakeningCost(6000, 600, 0) &&
              caps.Awakening(120, "UR") == new AwakeningCost(4500, 450, 225), "seed awakening costs match ship_level.lua");
    }

    private static string? FindUp(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static LevelingStage D3() => new()
    {
        Key = "d3", Label = "D3", EnemyLevel = 104, LuaExp = 702, ObservedSRankExp = 842
    };

    private static IEnumerable<LevelingShip> Leeches()
    {
        yield return Ship("m-goal", "Main", 99, morale: 134) with { IsGrowthGoal = true };
        yield return Ship("m-content", "Main", 103, morale: 110) with { ContentReach = 4 };
        yield return Ship("m-farm", "Main", 103) with { FarmFleetId = 1 };
        yield return Ship("v-goal", "Vanguard", 112) with { IsGrowthGoal = true, FarmFleetId = 1 };
        yield return Ship("v-farm", "Vanguard", 113) with { FarmFleetId = 1 };
        yield return Ship("v-other", "Vanguard", 110) with { BestRatingScore = 44 };
    }

    private static IEnumerable<LevelingShip> Drivers()
    {
        yield return Ship("lead-strong", "Vanguard", 125) with { MobRatingScore = 52, SelfSurvival = 150 };
        yield return Ship("lead-farm", "Vanguard", 125) with { MobRatingScore = 34, SelfSurvival = 100 };
        yield return Ship("gun", "Main", 125, morale: 110) with { MobRatingScore = 52, AoeDamage = 6 };
    }

    private static void CheckLiveState(ShipLevelCaps caps)
    {
        var inputs = new FleetPlannerInputs
        {
            LevelingShips =
            [
                Ship("growing", "Main", 103),
                Ship("capped", "Main", 105, experience: 5000),
                Ship("stuck", "Vanguard", 70, rarity: "SR") with { TemplateMaxLevel = 70 },
                Ship("driver", "Vanguard", 120),
                Ship("elsewhere", "Main", 90)
            ],
            FarmFleetId = 1,
            FarmFleetUids = ["growing", "capped", "stuck", "driver"]
        };
        var leveling = LiveStateBuilder.Leveling(inputs, caps);
        Check(leveling.FarmFleetId == 1 && leveling.Ships.Select(ship => ship.Uid).SequenceEqual(["capped", "growing", "stuck"]),
            "live state lists the farming fleet's ships under Lv.120");
        Check(leveling.Growing == 1 && leveling.NextStopReached &&
              leveling.Ships.Where(ship => ship.Waiting).Select(ship => ship.Uid).SequenceEqual(["capped", "stuck"]),
            "full EXP on an awakening cap and a limit-break cap stop a ship");
        Check(LiveStateBuilder.Leveling(new FleetPlannerInputs { LevelingShips = inputs.LevelingShips }, caps).Ships.Count == 0,
            "no farming fleet, no leveling block");

        var battle = new LatestBattleInfo(1792, 1204090, "12-4", 4, "53327714", "타이거", 1);
        var document = LiveStateBuilder.Build(new DateTimeOffset(2026, 10, 18, 21, 0, 0, TimeSpan.FromHours(9)), true,
            battle, 1204, inputs, caps);
        Check(document.Schema == 1 && document.WrittenAt == "2026-10-18T21:00:00.000+09:00" &&
              document.WrittenUnix == 1792324800, "live state time stamps");
        Check(document.Battle is { LastId: 1792, Rank: "S", ShipDrops: 1 } && document.Chapter.Id == 1204,
            "live state battle and chapter");
        Check(LiveStateBuilder.Build(DateTimeOffset.UnixEpoch, false, null, 0, inputs, caps).Battle is { LastId: 0, Score: -1 },
            "no battle yet reads as id 0 so macros can compare with it");
        Check(new LatestBattleInfo(1, 1, "", 3, "", "", 0).Rank == "A" &&
              new LatestBattleInfo(1, 1, "", -1, "", "", 0).Rank == "", "battle score to rank");
    }

    private static LevelingShip Ship(string uid, string position, int level, int morale = 134, int experience = 0,
        string rarity = "UR") => new()
    {
        Uid = uid,
        Name = $"{uid} 이름",
        Rarity = rarity,
        Hull = position == "Main" ? "BB" : position == "Vanguard" ? "CL" : "SS",
        Position = position,
        Level = level,
        Experience = experience,
        Morale = morale,
        IsLocked = true
    };

    // Levels 1-124 cost 1,000 EXP (UR 1,200); 100 and 105 are caps with awakening costs.
    private static JsonElement LevelTable()
    {
        var rows = new Dictionary<string, object>();
        for (var level = 1; level <= 125; level++)
        {
            var row = new Dictionary<string, object>
            {
                ["level"] = level, ["exp"] = level == 125 ? 0 : 1000, ["exp_ur"] = level == 125 ? 0 : 1200,
                ["level_limit"] = level is 100 or 105 ? 1 : 0
            };
            if (level == 105)
            {
                row["awaken"] = new Dictionary<string, object>
                {
                    ["5"] = new Dictionary<string, int> { ["gold"] = 4000, ["15008"] = 400 },
                    ["6"] = new Dictionary<string, int> { ["gold"] = 6000, ["15008"] = 600 }
                };
            }

            rows[level.ToString(System.Globalization.CultureInfo.InvariantCulture)] = row;
        }

        return JsonSerializer.SerializeToElement(rows);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
