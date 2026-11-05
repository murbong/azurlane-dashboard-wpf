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
        Console.WriteLine("Gear allocation checks passed.");
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
