namespace AzurLaneDashboard.Core.Models;

/// <summary>
/// Oil followed from packets between the server's resource syncs (SC 11003 at login, SC 11004 when it carries
/// oil): chapter entries, battles and commissions cost what the client charges; rewards with oil and oil-field
/// harvests (the field's stock at the sync) add. Right after a sync <see cref="Oil"/> equals
/// <see cref="SyncedOil"/>; after that it misses what no captured packet states, so it is an estimate.
/// </summary>
public sealed record OilEstimate(int Oil, int SyncedOil, long SyncedRowId, int Battles, int Spent, int Gained);

/// <summary>
/// The courtyard's food the way the client counts it (Dorm:getFoodLeftTime): <see cref="BaseFood"/> from SC 19001
/// at login or the last settlement (SC 19027) plus snacks used since, less <see cref="PerTick"/> at
/// <see cref="NextTick"/> and every <see cref="TickSeconds"/> after it while ships train on floor 1.
/// <see cref="Snacks"/> holds snack counts (item id to count) from the item list less the snacks used since.
/// </summary>
public sealed record DormFoodState(
    int BaseFood,
    int Capacity,
    int TrainingShips,
    int PerTick,
    int TickSeconds,
    long NextTick,
    long BaseRowId,
    IReadOnlyDictionary<int, int> Snacks)
{
    /// <summary>Snack item ids and the food each adds (item_data_statistics usage_arg).</summary>
    public static readonly IReadOnlyDictionary<int, int> SnackFood = new Dictionary<int, int>
    {
        [50001] = 1000, [50002] = 2000, [50003] = 3000, [50004] = 5000, [50005] = 10000, [50006] = 20000
    };

    public int FoodAt(DateTimeOffset now)
    {
        var seconds = now.ToUnixTimeSeconds();
        if (TrainingShips == 0 || PerTick <= 0 || TickSeconds <= 0 || seconds < NextTick) return BaseFood;
        var ticks = (seconds - NextTick) / TickSeconds + 1;
        return (int)Math.Max(0, BaseFood - ticks * PerTick);
    }

    public double FillAt(DateTimeOffset now) => Capacity > 0 ? Math.Min(1, (double)FoodAt(now) / Capacity) : 0;
}
