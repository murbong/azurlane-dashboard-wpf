using System.Globalization;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>Builds live_state.json from the loaded snapshot; see <see cref="LiveStateDocument"/>.</summary>
public static class LiveStateBuilder
{
    public static LiveStateDocument Build(
        DateTimeOffset now,
        bool captureRunning,
        LatestBattleInfo? battle,
        int chapterId,
        FleetPlannerInputs inputs,
        ShipLevelCaps caps,
        OilEstimate? oil = null,
        DormFoodState? dorm = null)
    {
        return new LiveStateDocument(
            LiveStateDocument.CurrentSchema,
            now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
            Math.Round(now.ToUnixTimeMilliseconds() / 1000.0, 3),
            new LiveCaptureState(captureRunning),
            battle is null
                ? new LiveBattleState(0, 0, "", "", -1, "", "", 0)
                : new LiveBattleState(battle.RowId, battle.StageId, battle.StageLabel, battle.Rank, battle.Score,
                    battle.MvpUid, battle.MvpName, battle.ShipDrops),
            new LiveChapterState(chapterId),
            Leveling(inputs, caps),
            oil is null
                ? new LiveResourcesState(null, null, false, 0)
                : new LiveResourcesState(oil.Oil, oil.SyncedOil, oil.Battles > 0 || oil.Spent > 0 || oil.Gained > 0,
                    oil.Battles),
            Dorm(now, dorm));
    }

    public static LiveDormState Dorm(DateTimeOffset now, DormFoodState? dorm)
    {
        if (dorm is null)
            return new LiveDormState(null, null, null, 0, new Dictionary<string, int>());

        var snacks = DormFoodState.SnackFood.ToDictionary(
            pair => pair.Value.ToString(CultureInfo.InvariantCulture),
            pair => dorm.Snacks.GetValueOrDefault(pair.Key));
        return new LiveDormState(dorm.FoodAt(now), dorm.Capacity, Math.Round(dorm.FillAt(now), 4), dorm.TrainingShips,
            snacks);
    }

    public static LiveLevelingState Leveling(FleetPlannerInputs inputs, ShipLevelCaps caps)
    {
        if (inputs.FarmFleetId <= 0)
            return new LiveLevelingState(0, 0, false, []);

        var fleet = new HashSet<string>(inputs.FarmFleetUids, StringComparer.Ordinal);
        var ships = inputs.LevelingShips
            .Where(ship => fleet.Contains(ship.Uid) && ship.Level is > 0 and < LevelingPlanBuilder.ReadyLevel)
            .Select(ship => new LiveLevelingShip(ship.Uid, ship.Name, ship.Level, IsStopped(ship, caps)))
            .OrderBy(ship => ship.Uid, StringComparer.Ordinal)
            .ToList();
        return new LiveLevelingState(inputs.FarmFleetId, ships.Count(ship => !ship.Waiting),
            ships.Any(ship => ship.Waiting), ships);
    }

    // No more levels until the player acts: full EXP on an awakening cap, or the limit-break cap reached.
    private static bool IsStopped(LevelingShip ship, ShipLevelCaps caps) =>
        caps.IsWaitingForAwakening(ship.Level, ship.Experience, ship.Rarity) ||
        ship.TemplateMaxLevel is > 0 and < 100 && ship.Level >= ship.TemplateMaxLevel;
}
