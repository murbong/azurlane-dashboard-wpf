namespace AzurLaneDashboard.Core.Services;

/// <summary>
/// Slot order inside a fleet. The battle code makes the first main ship the flagship (BattleFleetVO.appendMainUnit)
/// and the first vanguard the leader that the enemy reaches first (BattleFleetVO: _leaderUnit = _scoutList[1]),
/// so a recommendation's order is a placement, not just a list. Both rules read Regal's own fields (its Guidelines
/// legend, checked 2026-11-06):
/// <list type="bullet">
/// <item>"Flag Req" (Flag Requirements): what a main ship loses away from the flagship, -1 weak buffs or barrage
/// parts ... -5 strong buffs and barrages; none or 0 means the position does not matter. Regal marks -2 and below
/// "Needs flagship position".</item>
/// <item>"Self Survival" (eHP): for vanguards, tankiness as a percentage of Portland (100), linear. Main ships use a
/// separate -5..5 bonus scale, so only vanguards are compared on it.</item>
/// </list>
/// </summary>
public static class FleetSlotOrder
{
    public const string SurvivalMetric = "Self Survival";
    public const string FlagMetric = "Flag Req";

    // Regal's descriptions (not its numbers) say these vanguards should not be the main tank in front.
    private static readonly string[] OffLeadNames = ["Blücher", "Seattle", "Le Triomphant", "Le Hardi", "Miller"];

    // ...and these main ships do better away from the flagship position. The carrier Amagi only: the battlecruiser
    // Amagi wants the flagship (Flag Req -1).
    private static readonly string[] OffFlagNames = ["Amagi(CV)", "Duncan", "Ulrich von Hutten"];

    /// <summary>Regal's vanguard eHP, or 0 when unknown. Values of 1 and below belong to the main-fleet scale.</summary>
    public static double Survival(FleetPlannerShip ship) =>
        ship.Metrics.GetValueOrDefault(SurvivalMetric) is var value && value > 1 ? value : 0;

    /// <summary>How much a main ship needs the flagship position, 0 (does not matter) to 5.</summary>
    public static int FlagNeed(FleetPlannerShip ship) =>
        ship.Metrics.GetValueOrDefault(FlagMetric) is var value && value < 0 ? (int)Math.Min(5, -value) : 0;

    /// <summary>
    /// Moves the most survivable vanguard to the front; the others keep their order. Nothing moves when no ship has
    /// a score, and a ship Regal keeps off the front never takes it.
    /// </summary>
    public static List<T> LeadFirst<T>(IReadOnlyList<T> vanguard, Func<T, FleetPlannerShip?> ship)
    {
        var ordered = vanguard.ToList();
        var best = BestIndex(ordered, ship, candidate => IsNamed(candidate, OffLeadNames) ? 0 : Survival(candidate));
        if (best < 0 && ordered.Count > 1 && ship(ordered[0]) is { } first && IsNamed(first, OffLeadNames))
            best = ordered.FindIndex(item => ship(item) is { } other && !IsNamed(other, OffLeadNames));
        return MoveToFront(ordered, best);
    }

    /// <summary>
    /// Moves the main ship that needs the flagship most to the front. When none needs it, a ship Regal keeps off the
    /// flagship steps back so it is not left there by default.
    /// </summary>
    public static List<T> FlagshipFirst<T>(IReadOnlyList<T> main, Func<T, FleetPlannerShip?> ship)
    {
        var ordered = main.ToList();
        var best = BestIndex(ordered, ship, candidate => FlagNeed(candidate));
        if (best < 0 && ordered.Count > 1 && ship(ordered[0]) is { } first && IsNamed(first, OffFlagNames))
            best = ordered.FindIndex(item => ship(item) is { } other && !IsNamed(other, OffFlagNames));
        return MoveToFront(ordered, best);
    }

    public static string LeadText(FleetPlannerShip ship) =>
        Survival(ship) > 0 ? $"선두 · 생존력 {Survival(ship):0}" : "선두";

    /// <summary>The flagship label when Regal says the ship needs the spot; empty when the spot does not matter.</summary>
    public static string FlagshipText(FleetPlannerShip ship) =>
        FlagNeed(ship) > 0 ? $"★ 기함 · 기함 필요도 {FlagNeed(ship)}/5" : "";

    private static int BestIndex<T>(List<T> ordered, Func<T, FleetPlannerShip?> ship, Func<FleetPlannerShip, double> value)
    {
        var best = -1;
        var bestValue = 0.0;
        for (var index = 0; index < ordered.Count; index++)
        {
            if (ship(ordered[index]) is not { } candidate) continue;
            var current = value(candidate);
            if (current > bestValue)
            {
                best = index;
                bestValue = current;
            }
        }

        return best;
    }

    private static List<T> MoveToFront<T>(List<T> ordered, int index)
    {
        if (index <= 0) return ordered;
        var item = ordered[index];
        ordered.RemoveAt(index);
        ordered.Insert(0, item);
        return ordered;
    }

    // Regal uids are "id_English name"; retrofits and pair variants add a suffix in parentheses.
    private static bool IsNamed(FleetPlannerShip ship, IEnumerable<string> names)
    {
        var separator = ship.RegalUid.IndexOf('_', StringComparison.Ordinal);
        var name = separator >= 0 ? ship.RegalUid[(separator + 1)..] : ship.RegalUid;
        return names.Any(candidate => name == candidate || name.StartsWith(candidate + " (", StringComparison.Ordinal));
    }
}
