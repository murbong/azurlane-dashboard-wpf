namespace AzurLaneDashboard.Core.Models;

/// <summary>A ship placed in the courtyard (backyard dorm).</summary>
public sealed record DormShip(string Uid, string Name, string IconUrl, string Rarity, int Level, int Floor);

/// <summary>
/// Courtyard placement from the last login list (SC 19001) and the placements after it. Floor 1 is the training
/// floor (EXP and morale, eats food); floor 2 is the rest floor, which restores morale faster and gives no EXP.
/// </summary>
public sealed class DormState
{
    public const int TrainingFloor = 1;
    public const int RestFloor = 2;

    public bool IsKnown { get; init; }
    /// <summary>Ships per floor (exp_pos); the game gives the rest floor as many as the training floor.</summary>
    public int SlotsPerFloor { get; init; }
    /// <summary>Unlocked floors (floor_num).</summary>
    public int FloorCount { get; init; }
    public IReadOnlyDictionary<string, DormShip> Ships { get; init; } = new Dictionary<string, DormShip>();
    /// <summary>The farming fleet's ships in fleet order, with their floor (0 when outside the courtyard).</summary>
    public IReadOnlyList<DormShip> FarmFleet { get; init; } = [];
    /// <summary>Game fleet id of the farming fleet, 0 when unknown.</summary>
    public int FarmFleetId { get; init; }

    public int CountOn(int floor) => Ships.Values.Count(ship => ship.Floor == floor);
}

/// <summary>One ship line of the 숙소 card.</summary>
public sealed class DormRow
{
    public string Uid { get; init; } = "";
    public string Name { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string SubText { get; init; } = "";
    public string StatusText { get; init; } = "";
    /// <summary>"in" (where it should be), "next" (a move to make), "out" (needs attention) or "wait" (neutral).</summary>
    public string StatusKind { get; init; } = "wait";
}

public sealed class DormPlan
{
    public static DormPlan Unknown { get; } = new();

    public bool IsKnown { get; init; }
    public string HeaderText { get; init; } = "";
    public string RestTitle { get; init; } = "";
    public IReadOnlyList<DormRow> RestRows { get; init; } = [];
    public string RestAdvice { get; init; } = "";
    public string TrainTitle { get; init; } = "";
    public IReadOnlyList<DormRow> TrainRows { get; init; } = [];
    /// <summary>Ships on the training floor now that the recommendation would not pick.</summary>
    public IReadOnlyList<DormRow> TrainOthers { get; init; } = [];
    public string TrainAdvice { get; init; } = "";

    public bool IsUnknown => !IsKnown;
    public bool HasTrainOthers => TrainOthers.Count > 0;
    public bool HasRestAdvice => RestAdvice.Length > 0;
    public bool HasTrainAdvice => TrainAdvice.Length > 0;
}
