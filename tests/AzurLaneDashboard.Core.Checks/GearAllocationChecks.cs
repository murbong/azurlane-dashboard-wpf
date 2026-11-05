using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

internal static class GearAllocationChecks
{
    public static void Run()
    {
        var map = new RegalGearMap(Json("""
            {
              "ship": { "1": { "M": 10, "L/M/H": 11 }, "2": { "L/M/H": 20, "Flag/Bottom": 21, "Top": 22 }, "3": { "C": 30 } },
              "usecase": { "10": [100, 101, 102, 103, 104], "11": [110, 101, 102, 103, 104] },
              "slot": { "100": [1, 2, 3], "101": [4, 5], "102": [6, 7], "103": [8, 9], "104": [8, 10], "110": [11, 2] },
              "row": { "1": [1], "2": [2, 3], "3": [4], "4": [5], "5": [6], "6": [7], "7": [8], "8": [9], "9": [10],
                       "10": [11], "11": [12] },
              "equip": { "1": { "id": 500 }, "2": { "id": 600 }, "3": [ { "id": 601 }, { "id": 601 } ], "4": { "id": 700 },
                         "5": { "id": 800 }, "6": { "id": 810 }, "7": { "id": 900 }, "8": { "id": 910 },
                         "9": { "id": 950 }, "10": { "id": 960 }, "11": { "id": 970 }, "12": { "id": 600 } }
            }
            """));
        CheckMap(map);

        var a = new GearFleetShip("a", "A 이름", 1, true, new Dictionary<int, GearItemCopy>
        {
            [1] = new(700, 10), [2] = new(810, 13)
        });
        var b = new GearFleetShip("b", "B 이름", 1, false, new Dictionary<int, GearItemCopy> { [2] = new(800, 10) });
        var inventory = new GearInventory
        {
            Spare = new Dictionary<int, IReadOnlyList<int>>
            {
                [601] = [0], [900] = [13], [950] = [10], [970] = [0]
            },
            Equipped =
            [
                new GearHolder("x", "X 이름", 1, 500, 10, 125, InSortieFleet: true),
                new GearHolder("y", "Y 이름", 3, 500, 0, 80, InSortieFleet: false),
                new GearHolder("b", "B 이름", 2, 800, 10, 125, InSortieFleet: true)
            ]
        };

        var plans = GearAllocator.Allocate([a, b], map, "M", inventory, GearSourceMode.Move, alternateUsecase: "H");
        var first = plans[0].Slots;
        Check(first[0] is { Action: GearSlotAction.Move, BaseId: 500, Rank: 1, CurrentRank: 3 } move &&
              move.MoveFrom?.ShipUid == "y", "the best piece moves over from a ship outside any sortie fleet first");
        Check(first[0].BagAlternative is { Action: GearSlotAction.Bag, BaseId: 601, Rank: 2 },
            "Move mode also says what the warehouse alone would give");
        Check(first[1] is { Action: GearSlotAction.Keep, BaseId: 810, Rank: 2, BlockedRank: 1 } keep &&
              keep.BlockedBy.SequenceEqual(["B 이름"]), "a piece used by another ship of the fleet is never taken");
        Check(first[2] is { Action: GearSlotAction.Bag, BaseId: 900, Enhance: 13 }, "a spare fills an empty slot, best copy first");
        Check(first[3] is { Action: GearSlotAction.Bag, BaseId: 950 } && first[4] is { Action: GearSlotAction.Bag, BaseId: 970, Rank: 2 },
            "two slots wanting one spare: the first gets it, the second settles lower");
        Check(plans[0].ChangeCount == 4 && plans[0].SlotsDifferingInAlternateArmor.SequenceEqual([1]),
            "change count and the slot whose ranking differs for the other armor");

        var second = plans[1].Slots;
        Check(second[0] is { Action: GearSlotAction.Move } sortie && sortie.MoveFrom is { ShipUid: "x", InSortieFleet: true },
            "the next ship can still take a copy from a sortie fleet ship, flagged as such");
        Check(second[1] is { Action: GearSlotAction.Keep, Rank: 1 }, "an equipped best piece stays");
        Check(second[2] is { Action: GearSlotAction.Missing, BaseId: 900, Rank: 1 } && second[3].Action == GearSlotAction.Missing,
            "an empty slot with nothing reachable reports the missing best piece");

        var bagOnly = GearAllocator.Allocate([a, b], map, "M", inventory, GearSourceMode.Bag);
        Check(bagOnly.SelectMany(plan => plan.Slots).All(slot => slot.Action != GearSlotAction.Move),
            "warehouse mode never moves gear between ships");
        Check(bagOnly[0].Slots[0] is { Action: GearSlotAction.Bag, BaseId: 601 }, "warehouse mode takes the best spare");

        var unknown = new GearFleetShip("u", "U 이름", 99, false, new Dictionary<int, GearItemCopy> { [1] = new(1, 0) });
        var noData = GearAllocator.Allocate([unknown], map, "M", inventory, GearSourceMode.Move)[0];
        Check(noData.Slots[0].Action == GearSlotAction.Keep && noData.Slots[1].Action == GearSlotAction.NoData,
            "ships without community data keep their gear");
        CheckSlotRules();
        CheckAugments();
        Console.WriteLine("Gear allocation checks passed.");
    }

    /// <summary>
    /// Special equipment ranks come from the same Regal table under the ship's "aug" key, but their ids overlap
    /// with gear ids, so a gear spare and an augment spare with one id must never stand in for each other.
    /// </summary>
    private static void CheckAugments()
    {
        var map = new RegalGearMap(Json("""
            {
              "ship": { "7": { "H": 70 }, "8": { "H": 80 } },
              "usecase": { "70": [700], "80": [800, 801, 801, 801, 801] },
              "slot": { "700": [1, 2], "800": [1], "801": [] },
              "row": { "1": [1], "2": [2] },
              "equip": { "1": { "id": 8000 }, "2": { "id": 9000 } }
            }
            """));
        var a = new GearFleetShip("a", "A 이름", 8, true, new Dictionary<int, GearItemCopy>(), RegalAugmentId: 7);
        var b = new GearFleetShip("b", "B 이름", 99, false, new Dictionary<int, GearItemCopy>(), RegalAugmentId: 7,
            CurrentAugment: new GearItemCopy(8000, 5));
        var inventory = new GearInventory
        {
            Spare = new Dictionary<int, IReadOnlyList<int>> { [8000] = [0] },
            AugmentSpare = new Dictionary<int, IReadOnlyList<int>> { [9000] = [3] },
            AugmentEquipped = [new GearHolder("b", "B 이름", GearAllocator.AugmentSlot, 8000, 5, 120, InSortieFleet: true)]
        };

        var plans = GearAllocator.Allocate([a, b], map, "H", inventory, GearSourceMode.Move);
        Check(plans[0].Slots[0] is { Action: GearSlotAction.Bag, BaseId: 8000 } &&
              plans[0].Augment is { Action: GearSlotAction.Bag, BaseId: 9000, Rank: 2, Enhance: 3, BlockedBaseId: 8000 },
            "special equipment uses its own pool and never takes a fleet mate's piece");
        Check(plans[1].Augment is { Action: GearSlotAction.Keep, BaseId: 8000, Rank: 1 } && plans[0].AugmentUsecase == "H",
            "an equipped best special piece stays");
        Check(plans[0].ChangeCount == 2, "the special slot counts as a change");

        var items = new Dictionary<int, GearItemInfo> { [8000] = new(8000, "주포", "", "UR", "gun") };
        var augmentItems = new Dictionary<int, GearItemInfo>
        {
            [8000] = new(8000, "특수A", "", "SSR", "special"),
            [9000] = new(9000, "특수B", "", "SR", "special")
        };
        var rows = GearPlanPresenter.Rows(plans[0], items, inventory, GearSourceMode.Move, augmentItems: augmentItems);
        Check(rows.Count == 6 && rows[5] is { SlotText: "SP", SlotLabel: "특수", Name: "특수B", ActionText: "창고 1개" } &&
              rows[5].SubText.Contains("1순위 특수A은(는) B 이름(같은 편성)가 사용 중", StringComparison.Ordinal) &&
              rows[0] is { SlotText: "1", Name: "주포" },
            "the panel adds a special equipment row named from its own item table");

        var none = GearAllocator.Allocate([a with { RegalAugmentId = 0 }], map, "H", inventory, GearSourceMode.Move)[0];
        Check(none.Augment is null && GearPlanPresenter.Rows(none, items, inventory, GearSourceMode.Move).Count == 5,
            "ships without a special ranking or piece show no special row");
    }

    /// <summary>
    /// Max Immelmann at her 1st break: slot 2 takes dive bombers (9) only, torpedo bombers (8) from the 2nd break,
    /// while Regal ranks a torpedo bomber first because it assumes a full limit break.
    /// </summary>
    private static void CheckSlotRules()
    {
        var map = new RegalGearMap(Json("""
            {
              "ship": { "5": { "C": 50 } },
              "usecase": { "50": [500, 501, 502, 502, 502] },
              "slot": { "500": [1, 2], "501": [1], "502": [] },
              "row": { "1": [1], "2": [2] },
              "equip": { "1": { "id": 8000 }, "2": { "id": 9000 } }
            }
            """));
        var rules = new GearSlotRules
        {
            Allowed = [new HashSet<int> { 9 }, new HashSet<int> { 9 }, new HashSet<int>(), new HashSet<int>(), new HashSet<int>()],
            UnlockLimitBreak = [new Dictionary<int, int> { [8] = 2 }, new Dictionary<int, int>(), new Dictionary<int, int>(),
                new Dictionary<int, int>(), new Dictionary<int, int>()]
        };
        var low = new GearFleetShip("m", "막스", 5, false, new Dictionary<int, GearItemCopy>(), rules);
        var types = new Dictionary<int, int> { [8000] = 8, [9000] = 9 };
        GearInventory Spares(params int[] baseIds) => new()
        {
            Spare = baseIds.ToDictionary(id => id, _ => (IReadOnlyList<int>)[0]),
            Types = types
        };

        var plan = GearAllocator.Allocate([low], map, "C", Spares(8000, 9000), GearSourceMode.Move)[0];
        Check(plan.Slots[0] is { Action: GearSlotAction.Bag, BaseId: 9000, Rank: 2, LockedBaseId: 8000, LockedRank: 1,
                LockedUntilLimitBreak: 2 },
            "a piece the slot cannot take yet is skipped for the best one it can, and remembered with its break");
        Check(plan.Slots[1] is { Action: GearSlotAction.NoData, BaseId: 0, LockedBaseId: 8000, LockedUntilLimitBreak: 0 },
            "a slot whose whole ranking is unequippable recommends nothing");

        var items = new Dictionary<int, GearItemInfo>
        {
            [8000] = new(8000, "뇌격기", "", "UR", "뇌격기"),
            [9000] = new(9000, "폭격기", "", "UR", "폭격기")
        };
        var rows = GearPlanPresenter.Rows(plan, items, Spares(8000, 9000), GearSourceMode.Move);
        Check(rows[0].SubText.Contains("1순위 뇌격기은(는) 2돌파부터 장착 가능", StringComparison.Ordinal) &&
              rows[1].SubText.Contains("이 칸에 장착 불가", StringComparison.Ordinal),
            "the panel says why the top piece is not recommended");

        var retrofitRules = new GearSlotRules
        {
            Allowed = rules.Allowed,
            UnlockLimitBreak = rules.UnlockLimitBreak,
            UnlockByRetrofit = [new HashSet<int>(), new HashSet<int> { 8 }, new HashSet<int>(), new HashSet<int>(), new HashSet<int>()]
        };
        var retrofitPlan = GearAllocator.Allocate([low with { SlotRules = retrofitRules }], map, "C", Spares(8000, 9000),
            GearSourceMode.Move)[0];
        Check(retrofitPlan.Slots[1] is { LockedBaseId: 8000, LockedUntilLimitBreak: 0, LockedUntilRetrofit: true } &&
              GearPlanPresenter.Rows(retrofitPlan, items, Spares(8000, 9000), GearSourceMode.Move)[1].SubText
                  .Contains("1순위 뇌격기은(는) 개조 후 장착 가능", StringComparison.Ordinal),
            "a piece only the retrofit can take says so");

        var missing = GearAllocator.Allocate([low], map, "C", Spares(), GearSourceMode.Move)[0];
        Check(missing.Slots[0] is { Action: GearSlotAction.Missing, BaseId: 9000, Rank: 2, LockedBaseId: 0 },
            "an empty slot names the best piece it can take, not the locked top piece");

        var full = GearAllocator.Allocate([low with { SlotRules = null }], map, "C", Spares(8000, 9000), GearSourceMode.Move)[0];
        Check(full.Slots[0] is { Action: GearSlotAction.Bag, BaseId: 8000, Rank: 1, LockedBaseId: 0 },
            "without slot data the plain ranking applies");
        Check(GearAllocator.Fits(low, 1, 7777, Spares()) && !GearAllocator.Fits(low, 1, 8000, Spares()),
            "pieces of unknown type always fit");
    }

    private static void CheckMap(RegalGearMap map)
    {
        Check(map.ResolveUsecase(1, "M", false) == "M" && map.ResolveUsecase(1, "H", false) == "L/M/H",
            "exact usecase first, then a combined key");
        Check(map.ResolveUsecase(2, "C-13+", true) == "Flag/Bottom" && map.ResolveUsecase(2, "C-13+", false) == "Top",
            "flagship and side layouts");
        Check(map.ResolveUsecase(3, "M", false) == "C" && map.ResolveUsecase(99, "M", false) == "", "chapter fallback; unknown ship");
        var slots = map.Slots(1, "M");
        Check(slots.Count == 5 && slots[0].Count == 3 && slots[0][1].SequenceEqual([600, 601]),
            "rows become ranks; list entries add alternatives once");
        Check(map.Slots(1, "L/M/H")[0].Select(rank => rank[0]).SequenceEqual([600, 601]),
            "an id listed again in a lower row keeps its best rank only");
    }

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
