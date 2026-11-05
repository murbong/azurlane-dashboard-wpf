using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

internal static class DormPlanChecks
{
    public static void Run()
    {
        var caps = new ShipLevelCaps(default);
        var dorm = new DormState
        {
            IsKnown = true,
            SlotsPerFloor = 2,
            FloorCount = 2,
            FarmFleetId = 1,
            Ships = new Dictionary<string, DormShip>
            {
                ["a"] = new("a", "A 이름", "", "SR", 90, DormState.RestFloor),
                ["x"] = new("x", "X 이름", "", "SR", 125, DormState.RestFloor),
                ["t1"] = new("t1", "T1 이름", "", "SR", 40, DormState.TrainingFloor),
                ["t2"] = new("t2", "T2 이름", "", "SR", 125, DormState.TrainingFloor)
            },
            FarmFleet = [new("a", "A 이름", "", "SR", 90, DormState.RestFloor), new("b", "B 이름", "", "SR", 80, 0)]
        };
        var ships = new List<LevelingShip>
        {
            Ship("c", 50) with { ContentReach = 1, ContentLabels = ["14 해역"] },
            Ship("t1", 40),
            Ship("t2", 125),
            Ship("r", 30) with { BestRatingScore = 99 },
            Ship("f", 60) with { ContentReach = 2, ContentLabels = ["EX 랭킹"] },
            Ship("u", 20) with { IsLocked = false, IsGrowthGoal = true }
        };

        var plan = DormPlanBuilder.Build(dorm, ships, new HashSet<string>(StringComparer.Ordinal) { "f" }, caps);
        Check(plan is { IsKnown: true, HeaderText: "1층 2/2 · 2층 2/2", RestTitle: "2층 휴식 · 1함대 1/2척" },
            "the card counts both floors and the farming fleet on floor 2");
        Check(plan.RestRows.Select(row => (row.StatusText, row.StatusKind)).SequenceEqual([("2층", "in"), ("숙소 밖", "out")]),
            "each farming ship shows its floor");
        Check(plan.RestAdvice == "2층이 가득 찼습니다. 쫄작 함대가 아닌 X 이름을(를) 빼고 B 이름을(를) 넣으세요.",
            "a full rest floor names the outsider to take out");
        Check(plan.TrainRows.Select(row => row.Uid).SequenceEqual(["c", "t1"]) &&
              plan.TrainRows[0] is { StatusText: "넣기", SubText: "Lv.50 · 14 해역" } &&
              plan.TrainRows[1] is { StatusText: "1층", SubText: "Lv.40 · 훈련 중" },
            "content picks come first, a current trainee keeps its seat over a rating-only pick, the farm fleet and unlocked ships stay out");
        Check(plan.TrainOthers.Count == 1 && plan.TrainOthers[0] is { Uid: "t2", StatusText: "EXP 못 받음", StatusKind: "out" },
            "a trainee at the top level is flagged");
        Check(plan.TrainAdvice.StartsWith("1층이 가득 찼습니다. T2 이름 대신 C 이름을(를) 넣으면", StringComparison.Ordinal),
            "a full training floor swaps out the ship that gains nothing first");

        Check(!DormPlanBuilder.Build(new DormState(), ships, new HashSet<string>(), caps).IsKnown,
            "no login list, no card");
        Console.WriteLine("Dorm plan checks passed.");
    }

    private static LevelingShip Ship(string uid, int level) => new()
    {
        Uid = uid, Name = $"{uid.ToUpperInvariant()} 이름", Level = level, IsLocked = true, Position = "Main", Rarity = "SR"
    };

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
