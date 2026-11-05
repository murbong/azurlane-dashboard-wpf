using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

internal static class FleetPlanChecks
{
    public static void Run()
    {
        var ships = new List<FleetPlannerShip>
        {
            Ship("m1", 1, "Main", 125, boss: "SS", mob: "S", heavy: 3000),
            Ship("m2", 2, "Main", 125, boss: "A", mob: "S", heavy: 2000) with
            {
                Pairings = [new FleetPlannerPairing([3], new Dictionary<string, string> { ["W15/16 Boss"] = "SS" })]
            },
            Ship("m3", 3, "Main", 125, boss: "S", mob: "A", heavy: 2500),
            Ship("m4", 4, "Main", 125, boss: "B", mob: "S", heavy: 900),
            Ship("v1", 11, "Vanguard", 125, boss: "S", mob: "S", heavy: 1000, offense: true) with { EmptySlots = 2 },
            Ship("v2", 12, "Vanguard", 125, boss: "S", mob: "S", heavy: 900),
            Ship("v3", 13, "Vanguard", 120, boss: "A", mob: "A", heavy: 800, skillGap: 2),
            Ship("v3-copy", 13, "Vanguard", 90, boss: "A", mob: "A", heavy: 800),
            Ship("v4", 14, "Vanguard", 125, boss: "B", mob: "S", heavy: 500),
            Ship("rookie", 15, "Vanguard", 110, boss: "S", mob: "B", heavy: 1500, experience: 1_000_000, sample: 1000),
            Ship("weak", 16, "Vanguard", 100, boss: "B", mob: "B", heavy: 400, experience: 2_000_000, sample: 1000),
            Ship("dud", 19, "Vanguard", 100, boss: "S", mob: "D", heavy: 500, experience: 2_000_000, sample: 1000),
            Ship("nosample", 17, "Main", 105, boss: "C", mob: "SS", light: 9000, experience: 2_000_000),
            Ship("sub", 18, "Submarine", 125, boss: "SS", mob: "SS", heavy: 9999)
        };
        var gameFleets = new List<IReadOnlySet<string>> { new HashSet<string> { "m1", "m3", "v1", "v2", "x" } };
        var plan = FleetPlanBuilder.Build(ships, gameFleets);
        var chapter = plan.Contents.Single(content => content.Key == "w1516");
        var boss = chapter.Fleets[0];
        var mob = chapter.Fleets[1];

        Check(plan.OwnedShipCount == 12 && plan.ReadyShipCount == 8, "duplicates and submarines are excluded from the roster");
        Check(boss.MainSlots.Select(slot => slot.ShipUid).SequenceEqual(["m1", "m3", "m2"]), "boss main fleet ranks by rating then damage");
        Check(boss.VanguardSlots.Select(slot => slot.ShipUid).SequenceEqual(["v1", "v2", "v3"]), "the higher-level copy represents a ship");
        var fleetUids = boss.MainSlots.Concat(boss.VanguardSlots).Concat(mob.MainSlots).Concat(mob.VanguardSlots)
            .Select(slot => slot.ShipUid).ToList();
        Check(fleetUids.Distinct().Count() == fleetUids.Count, "a ship never sits in both chapter fleets");
        Check(mob.MainSlots.Any(slot => slot.ShipUid == "m4") && !fleetUids.Contains("sub"), "mob fleet uses the remaining ships only");

        var paired = boss.MainSlots.Single(slot => slot.ShipUid == "m2");
        Check(paired.Rating == "SS" && paired.PartnerName == "m3 이름", "pair rating applies when the partner shares the fleet");
        Check(boss.GameFleetOverlap == 4 && !boss.MatchesGameFleet, "overlap counts ships shared with the game fleet");
        Check(boss.RoleSummary.Contains("공격 버프 1"), "role summary counts tagged ships");

        var top = chapter.Candidates[0];
        Check(top.ShipUid == "rookie" && top.ReplacesUid == "v3", "an under-levelled ship replaces the weakest ready slot");
        Check(top.RatingImproves && top.DamageDeltaPercent == 88, "candidate shows rating and damage gains");
        Check(top.EstimatedBattles == 1000 && top.CostText.Contains("100만"), "battle estimate rounds up from the ship's own sample");
        Check(boss.VanguardSlots.Single(slot => slot.ShipUid == "v3").UpgradeText.Contains("rookie 이름"), "replaced slot names its candidate");
        Check(chapter.Candidates.All(candidate => candidate.ShipUid != "dud"), "ships that would not improve the fleet are not suggested");
        Check(chapter.Candidates.Single(candidate => candidate.ShipUid == "weak").ReplacesText == "빈 자리를 채움",
            "a weak ship may still fill an empty slot");
        var noSample = chapter.Candidates.Single(candidate => candidate.ShipUid == "nosample");
        Check(noSample.EstimatedBattles is null && noSample.BasisText == "전투 표본 없음", "no invented battle estimate");
        Check(chapter.FinishItems.Any(item => item.ShipUid == "v3" && item.Detail == "Lv.120 → 125 · 스킬 2단계"),
            "finishing work lists level and skill gaps");
        Check(chapter.FinishItems.All(item => item.ShipUid != "v1") &&
              chapter.EquipmentGaps.Single().ShipUid == "v1" && chapter.EquipmentGaps[0].Detail == "빈칸 2",
            "missing gear is reported apart from level and skill goals");
        var unrated = plan.Contents.Single(content => content.Key == "ex");
        Check(unrated.Fleets.Count == 0 && !unrated.HasFleets, "contents without ratings show no empty fleet cards");

        using var changelog = JsonDocument.Parse("""
            {"dates":[
              {"date":"2026-10-10","changes":["Add Royal Fortune's and Vincenzo's unique augments.",
                                             "Add a setting that defaults hull filters to Main + Vanguard."]},
              {"date":"2026-09-26","ship_changes":["Actually apply Hardy's Chapter 15/16 Mob D → C mentioned in September 18.",
                                                  "Rename Hardy (with Hipper) to include Valparaíso."]},
              {"date":"2026-09-18","ship_changes":["Hardy: Chapter 15/16 Mob D → C. Borderline.",
                                                  "Valparaíso: Chapter 14 Boss A → S, Chapter 15/16 Mob S → A.",
                                                  "Admiral Hipper: Hard Arbiter B → A.", "Otto: new gun numbers."]},
              {"date":"2026-06-01","ship_changes":["Hardy: EX C → B."]}
            ]}
            """);
        var owned = new Dictionary<string, string>
        {
            ["Hardy"] = "하디", ["Valparaíso"] = "발파라이소", ["Hipper"] = "히퍼", ["Admiral Hipper"] = "어드미럴 히퍼",
            ["Royal Fortune"] = "로열 포춘", ["Vanguard"] = "뱅가드"
        };
        var changes = CommunityRatingChangeParser.Parse(changelog.RootElement, owned);
        Check(CommunityRatingChangeParser.LatestDate(changelog.RootElement) == "2026-10-10", "latest changelog date");
        Check(changes.Count == 4, "only owned ships within four weeks, without renames or repeated changes, are listed");
        Check(changes[0].ShipText == "로열 포춘" && changes[0].Parts.Single().Label == "고유 증강 평가 추가", "augment notes are summarised");
        Check(changes[1].Parts.Single() is { Label: "15·16 해역 잡몹", From: "D", To: "C", IsUp: true }, "possessive ship names match");
        var valparaiso = changes[2];
        Check(valparaiso.ShipText == "발파라이소" && valparaiso.Parts.Count == 2 && !valparaiso.Parts[1].IsUp,
            "several rating changes in one sentence");
        Check(changes[3].ShipText == "어드미럴 히퍼", "longer ship names win over contained names");
        CheckLeadOrder();
        Console.WriteLine("Fleet plan and rating changelog checks passed.");
    }

    // The battle code makes the first vanguard the leader, so the picked vanguard is ordered by survivability.
    private static void CheckLeadOrder()
    {
        FleetPlannerShip Vanguard(string uid, int group, string boss, double heavy, double survival) =>
            Ship(uid, group, "Vanguard", 125, boss, "S", heavy) with
            {
                Metrics = new Dictionary<string, double> { ["Heavy Dmg"] = heavy, ["Self Survival"] = survival }
            };
        var picked = FleetPlanBuilder.Build(
        [
            Ship("main", 1, "Main", 125, "S", "S", 2000),
            Vanguard("dps", 11, "SS", 3000, 40),
            Vanguard("tank", 12, "S", 900, 90),
            Vanguard("coded", 13, "S", 800, -2)
        ], []).Contents.Single(content => content.Key == "w1516").Fleets[0];
        Check(picked.VanguardSlots.Select(slot => slot.ShipUid).SequenceEqual(["tank", "dps", "coded"]) &&
              picked.VanguardSlots[0].PositionText == "선두 · 생존력 90" && !picked.VanguardSlots[1].HasPositionText,
            "the most survivable vanguard leads; the rest keep their rating order and negative codes are not scores");
        var unknown = FleetPlanBuilder.Build(
        [
            Ship("main", 1, "Main", 125, "S", "S", 2000),
            Ship("a", 11, "Vanguard", 125, "SS", "S", 3000),
            Ship("b", 12, "Vanguard", 125, "S", "S", 900)
        ], []).Contents.Single(content => content.Key == "w1516").Fleets[0];
        Check(unknown.VanguardSlots.Select(slot => slot.ShipUid).SequenceEqual(["a", "b"]) &&
              unknown.VanguardSlots[0].PositionText == "선두",
            "without survivability data nothing moves");

        // The first main ship is the flagship: Regal's flag requirement decides it, and nothing is claimed without one.
        FleetPlannerShip Main(string uid, int group, string boss, double heavy, double flag = 0, string regal = "") =>
            Ship(uid, group, "Main", 125, boss, "S", heavy) with
            {
                RegalUid = regal,
                Metrics = new Dictionary<string, double> { ["Heavy Dmg"] = heavy, ["Flag Req"] = flag }
            };
        var flagged = FleetPlanBuilder.Build(
        [
            Main("dps", 1, "SS", 3000), Main("needs", 2, "S", 2000, flag: -3), Main("minor", 3, "S", 1500, flag: -1),
            Ship("v", 11, "Vanguard", 125, "S", "S", 900)
        ], []).Contents.Single(content => content.Key == "w1516").Fleets[0];
        Check(flagged.MainSlots.Select(slot => slot.ShipUid).SequenceEqual(["needs", "dps", "minor"]) &&
              flagged.MainSlots[0].PositionText == "★ 기함 · 기함 필요도 3/5" && !flagged.MainSlots[2].HasPositionText,
            "the main ship that loses most away from the flagship takes it");
        var offFlag = FleetPlanBuilder.Build(
        [
            Main("amagi", 1, "SS", 3000, regal: "307154_Amagi(CV)"), Main("other", 2, "S", 2000),
            Ship("v", 11, "Vanguard", 125, "S", "S", 900)
        ], []).Contents.Single(content => content.Key == "w1516").Fleets[0];
        Check(offFlag.MainSlots.Select(slot => slot.ShipUid).SequenceEqual(["other", "amagi"]) &&
              offFlag.MainSlots.All(slot => !slot.HasPositionText),
            "a ship Regal keeps off the flagship steps back, and no flagship is claimed");
    }

    private static FleetPlannerShip Ship(string uid, int group, string position, int level, string boss, string mob,
        double heavy = 0, double light = 0, bool offense = false, int skillGap = 0, int? experience = 0, int sample = 0) => new()
    {
        Uid = uid,
        GroupKey = group,
        Name = $"{uid} 이름",
        Hull = position == "Main" ? "BB" : "DD",
        Position = position,
        Level = level,
        Ratings = new Dictionary<string, string> { ["W15/16 Boss"] = boss, ["W15/16 Mob"] = mob },
        Metrics = new Dictionary<string, double>
        {
            ["Heavy Dmg"] = heavy, ["Light Dmg"] = light > 0 ? light : heavy, ["Offense Buff"] = offense ? 1 : 0
        },
        ExperienceToReady = level >= FleetPlanBuilder.ReadyLevel ? 0 : experience,
        SampleExp = sample,
        SkillGap = skillGap
    };

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
