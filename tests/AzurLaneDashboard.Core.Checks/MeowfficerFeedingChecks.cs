using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

internal static class MeowfficerFeedingChecks
{
    public static void Run()
    {
        var catalog = new MeowfficerCatalog
        {
            Kinds = new Dictionary<int, MeowfficerKind>
            {
                [10] = new(10, "가", 5, 101, 30, false, "gamiao", 500, 1),
                [20] = new(20, "나", 4, 201, 30, false, "namiao", 300, 2)
            },
            Skills = new Dictionary<int, MeowfficerSkill>
            {
                [101] = new(101, "기술", 1, "", 102, 1), [102] = new(102, "기술", 2, "", 103, 3), [103] = new(103, "기술", 3, "", 0, 0)
            },
            Growth = new MeowfficerGrowth
            {
                Levels = Enumerable.Range(1, 30).ToDictionary(level => level, level => (0, level * 100, level * 100, level * 100))
            }
        };
        var cat = new OwnedMeowfficer("a", 10, 2, false, "", [], [], 101, 150);

        var battle = MeowfficerFeeding.Apply(catalog, [cat], [new MeowfficerFeed("a", [], 100)])[0];
        Check(battle is { Level: 3, Exp: 50, SkillId: 101 }, "battle EXP levels the cat (200 to reach Lv.3) and leaves the skill");

        // Same kind Lv.2 with 10 EXP: (500 + (100 + 10) × 0.7) × 1.2 = 692.4; another kind Lv.1: 300.
        var same = new OwnedMeowfficer("", 10, 2, false, "", [], [], 0, 10);
        var other = new OwnedMeowfficer("", 20, 1, false, "", [], [], 0, 0);
        Check(Math.Abs(MeowfficerFeeding.GivenExp(catalog, same, 1) - 692.4) < 0.001 &&
              MeowfficerFeeding.GivenExp(catalog, other, 1) == 300, "UpgradeCommanderCommand's EXP per eaten cat");
        var fed = MeowfficerFeeding.Apply(catalog, [cat], [new MeowfficerFeed("a", [same, other])])[0];
        // 150 + floor(992.4) = 1142: Lv.2→3 (200), 3→4 (300), 4→5 (400) leaves 242; one same-kind cat = skill EXP 1 → Lv.2.
        Check(fed is { Level: 5, Exp: 242, SkillId: 102, SkillExp: 0 }, "feeding levels the cat and the skill");
        Check(MeowfficerFeeding.Apply(catalog, [cat with { Level = 30 }], [new MeowfficerFeed("a", [], 999)])[0] is { Level: 30, Exp: 150 },
            "a cat at its top level gains nothing");
        Check(catalog.Kinds[10].IconUrl == "/static/game-assets/commandericon/gamiao.png", "the face comes from commandericon/{painting}");
        Console.WriteLine("Meowfficer feeding checks passed.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
