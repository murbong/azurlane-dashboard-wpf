using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

internal static class PersonalTaskChecks
{
    public static void Run()
    {
        PersonalTaskChain Chain(int group, int first) => new(group,
            Enumerable.Range(first, 3).Select(id => new PersonalTaskStep(id, $"단계 {id}", "{namecode:7}의 호감도를 100까지 달성한다",
                id == first + 1 ? 10000 : 1)).ToList());
        var chains = new[] { Chain(1, 100), Chain(2, 200), Chain(3, 300), Chain(4, 400), Chain(0, 500) };
        var tasks = new Dictionary<int, PlayerTask>
        {
            [100] = new(100, 1, 1, 10), [101] = new(101, 10000, 1, 11), [102] = new(102, 1, 1, 12),
            [200] = new(200, 1, 1, 10), [201] = new(201, 5124, 1, 0),
            [500] = new(500, 0, 1, 0)
        };
        var owned = new Dictionary<int, PersonalTaskShip>
        {
            [1] = new("가", "", "SSR", 120), [2] = new("나", "", "SSR", 100), [3] = new("다", "", "SR", 90)
        };
        var names = new Dictionary<int, string> { [4] = "라" };
        var plan = PersonalTaskBuilder.Build(chains, tasks, owned, names, "기준");
        PersonalTaskRow Row(int group) => plan.Rows.First(row => row.Group == group);

        Check(Row(1) is { Status: PersonalTaskStatus.Done, DoneSteps: 3 } && Row(4) is { Status: PersonalTaskStatus.Locked, ShipName: "라" },
            "every step submitted is done; without the ship it is locked");
        Check(Row(2) is { Status: PersonalTaskStatus.Active, DoneSteps: 1, ProgressText: "호감도 51/100" } &&
              Row(2).StepDesc == "나의 호감도를 100까지 달성한다",
            "the open step shows its progress with the ship's name filled in");
        Check(Row(3).Status == PersonalTaskStatus.Ready && Row(3).SubText.Contains("비서함", StringComparison.Ordinal),
            "an owned ship that never started says how to start");
        Check(Row(0) is { Status: PersonalTaskStatus.Active, ShipName: "단계" }, "an activity chain is named from its first step");
        Check(plan.Rows[0].Status == PersonalTaskStatus.Active && plan.Rows[^1].Status == PersonalTaskStatus.Locked,
            "in progress first, locked last");
        Check(!PersonalTaskBuilder.Build(chains, new Dictionary<int, PlayerTask>(), owned, names, "").HasData,
            "no task list, no page");
        Console.WriteLine("Character task checks passed.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
