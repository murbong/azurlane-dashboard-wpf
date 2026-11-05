using AzurLaneDashboard.Core.Models;

internal static class FleetCompositionChecks
{
    public static void Run()
    {
        // Two vanguards: the game stores the battlecruiser in slot 3, which must still sit in the back row.
        var fleet = new FleetCompositionGroupRow
        {
            FleetId = 3,
            Ships =
            [
                Ship(1, "전열", "로크"), Ship(2, "전열", "Z15"), Ship(3, "후열", "순양전함"), Ship(4, "후열", "전함"),
                Ship(5, "", "미확인")
            ]
        };
        var rows = fleet.FormationRows;
        var back = rows.Single(row => row.FormationName == "후열");
        var front = rows.Single(row => row.FormationName == "전열");
        Check(front.Slots.Select(slot => slot.ShipName).SequenceEqual(["로크", "Z15", ""]),
            "front row holds only vanguard hulls, padded to three slots");
        Check(back.Slots.Select(slot => slot.ShipName).SequenceEqual(["순양전함", "전함", "미확인"]),
            "main hulls stay in the back row whatever their slot; unknown ships fall back to the slot");
        Check(front.Slots[2].EmptyText == "전열 빈 자리", "empty positions are labelled without a fake slot number");

        var submarines = new FleetCompositionGroupRow { FleetId = 11, Ships = [Ship(1, "잠수", "U-47")] };
        Check(submarines.FormationRows.Single().FormationName == "잠수", "submarine fleets keep a single row");
        Console.WriteLine("Fleet composition formation checks passed.");
    }

    private static FleetCompositionShipRow Ship(int slot, string formation, string name) =>
        new() { FleetSlot = slot, FormationGroup = formation, ShipName = name, ShipUid = slot.ToString() };

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
