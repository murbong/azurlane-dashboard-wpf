using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using AzurLaneDashboard.Data.Services;

/// <summary>지휘냥: the seed maps agree with each other, the scorer follows the sources, and packets keep the cats current.</summary>
internal static class MeowfficerChecks
{
    public static void Run()
    {
        var catalog = SeedCatalog();
        CheckSeedMaps(catalog);
        CheckScoring(catalog);
        CheckFleetTypes();
        CheckPackets();
        Console.WriteLine("Meowfficer catalog, scoring and packet checks passed.");
    }

    private static MeowfficerCatalog SeedCatalog()
    {
        var maps = ProjectLocator.FindMapsDirectory();
        using var commanders = JsonDocument.Parse(File.ReadAllText(Path.Combine(maps, "commanders.json")));
        using var guide = JsonDocument.Parse(File.ReadAllText(Path.Combine(maps, "meowfficer_guide.json")));
        return MeowfficerCatalog.Parse(commanders.RootElement.Clone(), guide.RootElement.Clone());
    }

    private static void CheckSeedMaps(MeowfficerCatalog catalog)
    {
        Check(catalog.Kinds.Count == 34 && catalog.Talents.Count == 164 && catalog.Guide.Verified,
            "seed maps hold the 34 KR cats, 164 talents and a verified guide");
        Check(catalog.Kinds[10021] is { Name: "오구", Rarity: 5, FixedTalents: false } && catalog.Kinds[960011].FixedTalents,
            "names are trimmed and Tempesta cats have fixed talents");
        Check(catalog.Talents.Values.All(talent => talent.Group == talent.Id / 10),
            "every talent's line is its id divided by ten, which the packet writer relies on");
        Check(catalog.Guide.Roles.Keys.All(catalog.Kinds.ContainsKey), "every rated cat exists in the game data");
        Check(catalog.Guide.Roles.Values.All(role => catalog.Guide.FleetTypes.ContainsKey(role.Fleet)),
            "every role names a fleet type the guide defines");
        var groups = catalog.Talents.Values.Select(talent => talent.Group).ToHashSet();
        Check(catalog.Guide.FleetTypes.Values.SelectMany(type => type.Core.Concat(type.Filler).Concat(type.Special)).All(groups.Contains),
            "every prioritised talent line exists in the game data");
        Check(catalog.Guide.Sources.Any(source => source is { Role: "primary", Author: "Riceist", License: "CC BY-NC-SA 4.0" }),
            "the primary source is attributed with its licence");
    }

    private static void CheckScoring(MeowfficerCatalog catalog)
    {
        OwnedMeowfficer Cat(string uid, int template, int level, int[] talents, int[] origin) =>
            new(uid, template, level, true, "", talents, origin, catalog.Kinds[template].SkillId);
        var owned = new[]
        {
            Cat("antenna30", 10021, 30, [123, 2023, 5051, 2113], [121, 2021, 2112]),
            Cat("antenna1", 10021, 1, [411, 121, 2112], [411, 121, 2112]),
            Cat("lime1", 20011, 1, [632, 811, 5021], [632, 811, 5021]),
            Cat("drake", 960011, 30, [5131, 132, 5101, 903, 5141], [5141, 901]),
            Cat("every", 960041, 30, [5131, 152, 5101, 833, 5161], [5161, 831]),
            Cat("teach", 960061, 28, [322, 5101, 833, 5171], [5171, 831]),
            Cat("bisha30", 30011, 30, [632, 643, 2033, 5101], [2031, 641, 5101]),
            Cat("bisha5", 30011, 30, [923, 2021, 643, 5131, 5101], [921, 641, 5101]),
            Cat("full", 10021, 30, [123, 2023, 5051, 2113, 921], [121, 2021, 2112]),
            Cat("edel", 41021, 30, [511, 5131, 203, 2043], [2041, 201]),
            Cat("steel30", 40011, 30, [612, 903, 203, 5051], [901, 201, 5051]),
            Cat("steel21", 40011, 21, [701, 5101, 2142], [701, 5101, 2142])
        };
        var fleets = new[]
        {
            new MeowfficerFleetInput(1, ["CL", "CL", "DD", "CV", "BB", "BB"], "drake", "every"),
            new MeowfficerFleetInput(2, ["CB", "DD", "CB", "CV", "CV"], "", "bisha5"),
            new MeowfficerFleetInput(11, ["SSV", "SS"], "edel", "steel30")
        };
        var plan = MeowfficerPlanBuilder.Build(catalog, owned, fleets, farmFleetId: 1);
        var copies = plan.Copies.ToDictionary(copy => copy.Uid);

        var antenna = copies["antenna30"];
        Check(antenna is { Score: 6, Cap: 10, ChipText: "재능 6/10", RoleText: "전함 지휘" } &&
              antenna.Talents.Select(talent => talent.Kind).SequenceEqual(["core", "core", "off", "off"]),
            "a raised cat scores the battleship talents the source wants and strikes the rest");
        Check(antenna is { NextTalent: "생즉사 사즉생 (훈련 목록에 뜨면)", ReplaceTalent: "" },
            "the next talent is the source's top line the cat lacks; nothing is pushed out while a slot is free");
        Check(copies["full"].ReplaceTalent == "울프 하운드", "with five talents the cheapest unwanted one makes room");
        Check(copies["antenna1"] is { Verdict: MeowfficerVerdict.Feed, ChipText: "먹이 후보", IsRaiseCandidate: false },
            "one wanted starting point is below the rubric's two: material");
        Check(copies["lime1"] is { Verdict: MeowfficerVerdict.Good, IsRaiseCandidate: true },
            "three wanted starting points with 생즉사 is a Good start");
        Check(copies["drake"] is { Verdict: MeowfficerVerdict.Fixed, Score: null } &&
              copies["drake"].Talents.All(talent => talent.Kind == "fixed"),
            "Tempesta cats keep only the source's verdict");
        Check(copies["teach"] is { Verdict: MeowfficerVerdict.NoSource, HasRole: false, RoleText: "출처 평가 없음" },
            "a cat the source does not mention gets no rating, not a low one");
        Check(plan.Kinds[0].Copies.All(copy => copy.HasRole) && plan.Kinds[^1].TemplateId == 960061,
            "rated kinds come first, unrated last");

        var farm = plan.Fleets.Single(fleet => fleet.FleetId == 1);
        Check(farm is { TypeLabel: "전함", Purpose: "쫄작" } && farm.Slots[0] is { Status: "출처 역할과 맞음", StatusKind: "good" } &&
              farm.Slots[1] is { Status: "출처는 지휘 칸 추천", StatusKind: "info" },
            "the leveling fleet fights as battleships; Drake fits 지휘 and Every sits in the slot the source does not name");
        Check(farm.Slots[0].Candidates.Select(copy => copy.Uid).SequenceEqual(["antenna30"]),
            "one candidate per kind, the most raised copy");
        var carriers = plan.Fleets.Single(fleet => fleet.FleetId == 2);
        Check(carriers is { TypeLabel: "항모" } && carriers.Slots[0] is { Status: "비어 있음", HasNote: true } &&
              carriers.Slots[0].Candidates.FirstOrDefault()?.Uid == "bisha30",
            "a carrier fleet's empty 지휘 slot is filled by carrier talents, as the source advises");
        Check(carriers.Slots[1].Candidates.Select(copy => copy.Uid).SequenceEqual(["bisha30"]),
            "a better-trained copy of the seated kind stays as an upgrade");
        var subs = plan.Fleets.Single(fleet => fleet.FleetId == 11);
        Check(subs.Slots[0] is { Status: "출처는 참모 칸 추천", Note: "크레이를 지휘 칸으로 옮기면 출처 역할과 맞음", HasCandidates: false } &&
              subs.Slots[1].Note == "모허를 참모 칸으로 옮기면 출처 역할과 맞음",
            "two cats in each other's slots are told to swap, not offered a weaker copy");
    }

    private static void CheckFleetTypes()
    {
        Check(MeowfficerPlanBuilder.FleetType(11, ["SSV", "SS"]) == "SS" &&
              MeowfficerPlanBuilder.FleetType(2, ["DD", "CL", "CL", "CV", "CVL", "BB"]) == "CV" &&
              MeowfficerPlanBuilder.FleetType(3, ["CA", "DD", "BC", "BB"]) == "BB" &&
              MeowfficerPlanBuilder.FleetType(4, ["DD", "DD", "CL"]) == "DD_TORP",
            "fleet type follows the heavy ships, submarines by fleet id");
    }

    private static void CheckPackets()
    {
        using var db = new PacketDatabase();
        db.Apply("SC", 25001, """
            {"commanders":[
              {"id":1,"template_id":10021,"level":30,"is_locked":1,"ability":[121,2112],"ability_origin":[121,2112],"skill":[{"id":10023,"exp":0}],"used_pt":2,"name":"미제냥"},
              {"id":2,"template_id":20011,"level":1,"is_locked":0,"ability":[632,811,5021,212,2023],"ability_origin":[632,811,5021],"skill":[{"id":20011,"exp":0}],"used_pt":0},
              {"id":3,"template_id":10011,"level":1,"is_locked":0,"ability":[211],"ability_origin":[211],"skill":[{"id":10011,"exp":0}],"used_pt":0}
            ],"usage_count":0,"presets":[]}
            """);
        Check(db.Scalar("SELECT COUNT(*) FROM current_commanders") == 3, "the login list fills the table");

        db.Apply("CS", 25012, """{"commanderid":1,"targetid":122,"replaceid":0}""");
        db.Apply("SC", 25013, """{"result":0}""");
        Check(db.Text("SELECT abilities_json FROM current_commanders WHERE commander_uid = '1'") == "[2112,122]" &&
              db.Scalar("SELECT used_pt FROM current_commanders WHERE commander_uid = '1'") == 3,
            "learning a rank of a held line replaces it and spends one point");
        db.Apply("CS", 25012, """{"commanderid":2,"targetid":5101,"replaceid":811}""");
        db.Apply("SC", 25013, """{"result":0}""");
        Check(db.Text("SELECT abilities_json FROM current_commanders WHERE commander_uid = '2'") == "[632,5021,212,2023,5101]",
            "a full cat drops the replaced talent for the new one");
        db.Apply("CS", 25014, """{"commanderid":2}""");
        db.Apply("SC", 25015, """{"result":0}""");
        Check(db.Text("SELECT abilities_json FROM current_commanders WHERE commander_uid = '2'") == "[632,811,5021]" &&
              db.Scalar("SELECT used_pt FROM current_commanders WHERE commander_uid = '2'") == 0,
            "a reset returns to the starting talents");
        db.Apply("CS", 25016, """{"commanderid":2,"flag":1}""");
        db.Apply("SC", 25017, """{"result":0}""");
        db.Apply("CS", 25020, """{"name":"전함냥","commanderid":2}""");
        db.Apply("SC", 25021, """{"result":0}""");
        Check(db.Scalar("SELECT locked FROM current_commanders WHERE commander_uid = '2'") == 1 &&
              db.Text("SELECT name FROM current_commanders WHERE commander_uid = '2'") == "전함냥", "lock and rename");

        db.Apply("CS", 25006, """{"groupid":1,"pos":2,"commanderid":3}""");
        db.Apply("SC", 25007, """{"result":0}""");
        Check(db.Scalar("SELECT commander_id FROM current_fleet_commanders WHERE fleet_id = 1 AND pos = 2") == 3,
            "placing a cat in a fleet slot");
        db.Apply("CS", 25008, """{"targetid":1,"materialid":[3]}""");
        db.Apply("SC", 25009, """{"result":0}""");
        Check(db.Scalar("SELECT COUNT(*) FROM current_commanders WHERE commander_uid = '3'") == 0 &&
              db.Scalar("SELECT COUNT(*) FROM current_fleet_commanders WHERE commander_id = 3") == 0,
            "an eaten cat leaves the roster and its fleet slot");
        db.Apply("CS", 25012, """{"commanderid":1,"targetid":123,"replaceid":0}""");
        db.Apply("SC", 25013, """{"result":5}""");
        Check(db.Text("SELECT abilities_json FROM current_commanders WHERE commander_uid = '1'") == "[2112,122]",
            "a refused request changes nothing");

        db.Apply("CS", 25004, """{"boxid":1}""");
        db.Apply("SC", 25005, """{"result":0,"commander":{"id":4,"template_id":40011,"level":1,"ability":[5051],"ability_origin":[5051],"skill":[{"id":40011}]}}""");
        db.Apply("SC", 25039, """{"commander_list":[{"id":5,"template_id":41021,"level":1,"ability":[2041],"ability_origin":[2041],"skill":[{"id":41021}]}]}""");
        Check(db.Scalar("SELECT COUNT(*) FROM current_commanders") == 4, "a hatched cat and a pushed cat are added, not swapped in");

        var meowfficers = new NativeSnapshotService().Load(db.Path).Meowfficers;
        Check(meowfficers.Count == 4 && meowfficers.Copies.Single(copy => copy.Uid == "2") is { Nickname: "전함냥", Locked: true, Name: "제인" },
            "the snapshot reads the table and names the cats from the game data");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
