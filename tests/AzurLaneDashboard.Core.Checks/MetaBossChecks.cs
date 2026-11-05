using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

internal static class MetaBossChecks
{
    public static void Run()
    {
        var bosses = MetaBossCatalog.Parse(Json("""
            {
              "12": { "id": 12, "name": "진츠·META", "availability": "always", "windows": [], "armor": "Light",
                      "hull": "CL", "battle_seconds": 80,
                      "levels": [ { "level": 1, "hp": 50000, "enemy_id": 1, "expedition_id": 2 },
                                  { "level": 15, "hp": 1420000, "enemy_id": 3, "expedition_id": 4 } ],
                      "skills": [ { "name": "누적 피해", "grade": 2, "text": "50초 이후 받는 피해가 늘어난다." } ] },
              "20": { "id": 20, "name": "유다치·META", "availability": "stop", "windows": [], "armor": "Light", "hull": "DD" },
              "23": { "id": 23, "name": "브리스톨·META", "availability": "timed", "armor": "Light", "hull": "DD",
                      "windows": [ { "start": "2026-06-18T00:00:00", "end": "2026-09-17T12:00:00" } ] },
              "24": { "id": 24, "name": "새러토가·META", "availability": "timed", "armor": "Medium", "hull": "CV",
                      "battle_seconds": 80, "levels": [ { "level": 15, "hp": 1820000, "enemy_id": 5, "expedition_id": 6 } ],
                      "windows": [ { "start": "2026-09-17T00:00:00", "end": "2026-12-24T12:00:00" } ],
                      "skills": [ { "name": "체인지 배틀 뮤직", "grade": 1, "text": "장갑이 25초마다 바뀐다." } ] }
            }
            """));
        CheckAvailability(bosses);
        CheckGuides(bosses);
        CheckSeedMap();
        Console.WriteLine("META boss availability and guide checks passed.");
    }

    private static void CheckAvailability(IReadOnlyList<MetaBoss> bosses)
    {
        var now = new DateTime(2026, 10, 17, 9, 0, 0);
        Check(bosses.Count == 4 && MetaBossCatalog.Current(bosses, now)?.Id == 24, "the window holding now is the rotation boss");
        Check(MetaBossCatalog.Selectable(bosses, now).Select(boss => boss.Id).SequenceEqual([24, 12]),
            "rotation boss first, then the archive; hidden and finished bosses are left out");
        Check(MetaBossCatalog.Current(bosses, new DateTime(2026, 12, 24, 12, 0, 1)) is null, "a window ends at its end second");
        // 03:00 UTC is noon on the KR server, the last second of the window.
        Check(MetaBossCatalog.ServerTime(new DateTimeOffset(2026, 12, 24, 3, 0, 0, TimeSpan.Zero)) ==
              new DateTime(2026, 12, 24, 12, 0, 0), "server time is UTC+9");
        Check(MetaBossCatalog.DefaultSelection(bosses, now, savedBossId: 12)?.Id == 12, "a saved selectable choice wins");
        Check(MetaBossCatalog.DefaultSelection(bosses, now, savedBossId: 20)?.Id == 24, "a hidden saved boss falls back to the rotation");
        var later = new DateTime(2027, 1, 5);
        Check(MetaBossCatalog.DefaultSelection(bosses, later, gameArchiveBossId: 12)?.Id == 12 &&
              MetaBossCatalog.DefaultSelection(bosses, later)?.Id == 12, "between rotations the archive is offered");
        var saratoga = bosses.Single(boss => boss.Id == 24);
        Check(saratoga.TopHp == 1_820_000 && saratoga.TopLevel == 15, "top level HP");
    }

    private static void CheckGuides(IReadOnlyList<MetaBoss> bosses)
    {
        var (guides, rules) = MetaBossCatalog.ParseGuides(Json("""
            {
              "bosses": {
                "24": { "verified": true, "lastReviewed": "2026-10-17", "armorAlternates": ["Medium", "Heavy"],
                        "armorPeriodSeconds": 25,
                        "mechanics": [ { "title": "선두 록온", "text": "선두가 받는 피해 증가", "response": "선두에 탱커" } ],
                        "fleet": { "main": [ ["u_gotz"], ["u_azuchi", "u_meck"], ["u_missing", "u_lion"] ],
                                   "vanguard": [ ["u_traf"] ], "flagship": 0 },
                        "sources": [ { "title": "Guide", "url": "https://example.invalid/guide", "sourceUpdated": "2026-09-16" } ] },
                "12": { "verified": false, "fleet": { "main": [ ["u_meck"] ] } }
              },
              "regalStarRules": [ { "regalUid": "u_star", "hulls": ["CL", "DD"] } ]
            }
            """));
        Check(guides.Count == 2 && rules.Single().RegalUid == "u_star", "guides and star rules parse");

        var ships = new List<FleetPlannerShip>
        {
            Ship("gotz", "u_gotz", "Main", 125, "SS", medium: 5800, heavy: 4600),
            Ship("azuchi", "u_azuchi", "Main", 103, "SS", medium: 5200, heavy: 4500),
            Ship("meck", "u_meck", "Main", 125, "S", medium: 4800, heavy: 5000),
            Ship("lion", "u_lion", "Main", 125, "SS", medium: 5300, heavy: 4500),
            Ship("traf", "u_traf", "Vanguard", 125, "S", medium: 1150, heavy: 1280),
            Ship("star", "u_star", "Vanguard", 125, "S*", medium: 1100, heavy: 1100),
            Ship("plain", "u_plain", "Vanguard", 125, "S", medium: 1000, heavy: 1000),
            Ship("weak", "u_weak", "Vanguard", 125, "A", medium: 9000, heavy: 9000)
        };
        var now = new DateTime(2026, 10, 17, 9, 0, 0);
        var saratoga = bosses.Single(boss => boss.Id == 24);
        var plan = MetaBossPlanBuilder.Build(saratoga, guides[24], ships, rules, now,
            [new HashSet<string>(StringComparer.Ordinal) { "gotz", "lion", "x" }]);
        Check(plan.UsesGuide && plan.ArmorText == "중형 ↔ 중장갑 · 25초마다 교대" && plan.GearUsecase == "M",
            "a verified guide's armor alternation drives the damage figures and the gear usecase");
        Check(plan.DamageMetrics.SequenceEqual(["Medium Dmg", "Heavy Dmg"]), "both armors are scored");
        var main = plan.MainSlots;
        Check(main[0] is { Source: MetaBossSlotSource.Guide, IsFlagship: true } gotz && gotz.ShipUid == "gotz",
            "the guide's first choice takes the slot and the guide's flagship index");
        Check(main[1] is { Source: MetaBossSlotSource.GuideAlternative } meck && meck.ShipUid == "meck" &&
              meck.Note == "azuchi 이름 Lv.103 대신", "an owned but under-levelled first choice is explained");
        Check(main[2] is { Source: MetaBossSlotSource.GuideAlternative } lion && lion.Note == "1순위 미보유",
            "a missing first choice falls to the next");
        Check(plan.VanguardSlots[0]?.ShipUid == "traf" && plan.VanguardSlots[1]?.Source == MetaBossSlotSource.Auto,
            "slots the guide leaves open are filled automatically");
        // No rule covers a CV boss, so S* scores below S even though it deals more damage.
        Check(plan.VanguardSlots[1]?.ShipUid == "plain" && plan.VanguardSlots[2]?.ShipUid == "star",
            "an asterisk rating is discounted when its condition does not match the boss");
        Check(plan.Mechanics.Single().Response == "선두에 탱커" && plan.SourceText.Contains("원문 2026-09-16"),
            "guide mechanics and source");
        Check(plan.IsCurrent && plan.AvailabilityText == "12/24 12:00까지", "rotation end time");
        Check(plan.GameFleetOverlap == 2, "overlap with the closest game fleet");

        var jintsuu = bosses.Single(boss => boss.Id == 12);
        var unverified = MetaBossPlanBuilder.Build(jintsuu, guides[12], ships, rules, now);
        Check(!unverified.UsesGuide && unverified.Slots.All(slot => slot.Source == MetaBossSlotSource.Auto),
            "an unverified guide is ignored");
        Check(unverified.GearUsecase == "L" && unverified.Mechanics.Single().Title == "누적 피해",
            "light armor scoring and the game's own skill text stand in");
        Check(unverified.VanguardSlots.Select(slot => slot?.ShipUid).SequenceEqual(["traf", "star", "plain"]),
            "a matching star rule keeps the asterisk rating whole, so damage breaks the tie");
        Check(unverified.SourceText.StartsWith("공략 미검토", StringComparison.Ordinal) && unverified.AvailabilityText == "상시", "archive labels");

        // Placement: a verified guide names the flagship and the lead; a rating-filled fleet names neither, except
        // that its most survivable vanguard leads.
        Check(plan.VanguardSlots[0] is { IsLead: true, RoleText: "선두 · 공략 1순위" } && plan.MainSlots[0]!.IsFlagship,
            "the guide's lead and flagship keep their places");
        Check(unverified.Slots.All(slot => !slot.IsFlagship), "no flagship is claimed for a fleet picked by rating");
        var tanky = ships.Select(ship => ship.Uid == "plain"
            ? ship with { Metrics = new Dictionary<string, double>(ship.Metrics) { ["Self Survival"] = 95 } }
            : ship).ToList();
        var led = MetaBossPlanBuilder.Build(jintsuu, guides[12], tanky, rules, now);
        Check(led.VanguardSlots[0] is { ShipUid: "plain", IsLead: true, RoleText: "선두 · 생존력 95" } &&
              led.VanguardSlots.Skip(1).Select(slot => slot?.ShipUid).SequenceEqual(["traf", "star"]),
            "without a guide the most survivable vanguard leads and the rest keep their order");
        var flagged = ships.Select(ship => ship.Uid == "lion"
            ? ship with { Metrics = new Dictionary<string, double>(ship.Metrics) { ["Flag Req"] = -2 } }
            : ship).ToList();
        var placed = MetaBossPlanBuilder.Build(jintsuu, guides[12], flagged, rules, now);
        Check(placed.MainSlots[0] is { ShipUid: "lion", IsFlagship: true, RoleText: "★ 기함 · 기함 필요도 2/5" } &&
              placed.Slots.Count(slot => slot.IsFlagship) == 1,
            "without a guide the ship that needs the flagship takes it");
    }

    private static void CheckSeedMap()
    {
        string? path = null;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null && path is null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "maps", "meta_bosses.json");
            if (File.Exists(candidate)) path = candidate;
        }

        if (path is null) return;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var bosses = MetaBossCatalog.Parse(document.RootElement.Clone());
        Check(bosses.Count(boss => boss.IsArchive) == 18, "seed map has 18 archive bosses");
        var saratoga = bosses.Single(boss => boss.Id == 24);
        Check(saratoga is { Armor: "Medium", Hull: "CV", BattleSeconds: 80, TopHp: 1_820_000 } &&
              MetaBossCatalog.Current(bosses, new DateTime(2026, 10, 17))?.Id == 24, "seed Saratoga META matches the Lua data");
    }

    private static FleetPlannerShip Ship(string uid, string regalUid, string position, int level, string rating,
        double medium, double heavy) => new()
    {
        Uid = uid,
        RegalUid = regalUid,
        GroupKey = uid.GetHashCode(StringComparison.Ordinal),
        Name = $"{uid} 이름",
        Hull = position == "Main" ? "BB" : "DD",
        Position = position,
        Level = level,
        Ratings = new Dictionary<string, string> { ["META"] = rating },
        Metrics = new Dictionary<string, double>
        {
            ["Light Dmg"] = medium, ["Medium Dmg"] = medium, ["Heavy Dmg"] = heavy
        }
    };

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
