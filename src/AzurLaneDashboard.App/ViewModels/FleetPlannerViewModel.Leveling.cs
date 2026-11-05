using System.Globalization;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class FleetPlannerViewModel
{
    private ShipLevelCaps shipLevelCaps = new(default);
    private int? driverOverride;
    private string selectedStageKey;
    private LevelingStageChoice? selectedStage;
    private LevelingPlan? levelingPlan;
    private DormPlan dormPlan = DormPlan.Unknown;

    public BulkObservableCollection<LevelingStageChoice> StageChoices { get; } = [];
    public BulkObservableCollection<DriverChoice> DriverChoices { get; } = [];
    public BulkObservableCollection<LevelingQueueRow> FleetQueue { get; } = [];
    public BulkObservableCollection<LevelingQueueRow> WaitingQueue { get; } = [];
    public RelayCommand SetDriverCountCommand { get; }

    public LevelingPlan? LevelingPlan => levelingPlan;
    /// <summary>The 숙소 card: the farming fleet on floor 2 and the waiting ships for floor 1.</summary>
    public DormPlan DormPlan => dormPlan;
    public bool HasLevelingShips => levelingPlan?.HasShips == true;
    public bool HasNoLevelingShips => !HasLevelingShips;
    public bool HasWaitingQueue => WaitingQueue.Count > 0;
    public int? DriverOverride => driverOverride;
    public string SelectedStageKey => selectedStageKey;

    public LevelingStageChoice? SelectedStage
    {
        get => selectedStage;
        set
        {
            if (value is null || ReferenceEquals(value, selectedStage)) return;
            selectedStage = value;
            selectedStageKey = value.Key;
            OnPropertyChanged();
            RebuildLeveling();
            settingsChanged();
        }
    }

    /// <summary>The ships the leveling planner sees, for the macro page's fleet panel (icons and EXP).</summary>
    public IReadOnlyList<LevelingShip> LevelingShips => plan.Inputs.LevelingShips;

    public LiveStateDocument BuildLiveState(DateTimeOffset now, bool captureRunning, LatestBattleInfo? battle,
        int chapterId, OilEstimate? oil, DormFoodState? dorm) =>
        LiveStateBuilder.Build(now, captureRunning, battle, chapterId, plan.Inputs, shipLevelCaps, oil, dorm);

    private void RebuildStageChoices()
    {
        var choices = plan.Inputs.Stages.Select(option => new LevelingStageChoice(option)).ToList();
        StageChoices.ReplaceRange(choices);
        // The saved stage when it is still offered; otherwise the recent farming chapter (listed first).
        selectedStage = choices.FirstOrDefault(choice => choice.Key == selectedStageKey) ?? choices.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedStage));
    }

    private void RebuildLeveling()
    {
        var option = selectedStage?.Option;
        var goals = growthPlanner.LevelGoalShipUids();
        var ships = plan.Inputs.LevelingShips
            .Select(ship => goals.Contains(ship.Uid) ? ship with { IsGrowthGoal = true } : ship)
            .ToList();
        levelingPlan = option is null
            ? null
            : LevelingPlanBuilder.Build(ships, option.Stage, option.Records, shipLevelCaps, driverOverride,
                plan.Inputs.FarmFleetId, plan.Inputs.FarmFleetUids.ToHashSet(StringComparer.Ordinal));
        // Ships the planned farming fleet levels earn EXP in battle; the training floor is for the ones waiting.
        dormPlan = DormPlanBuilder.Build(plan.Inputs.Dorm, ships,
            levelingPlan?.Slots.Select(slot => slot.ShipUid).ToHashSet(StringComparer.Ordinal) ?? [], shipLevelCaps);

        // The 쫄작 tab lists the fleet and the next few; the 육성 계획 page lists the whole queue.
        var nearby = levelingPlan?.Queue.Take(LevelingPlanBuilder.QueueLength).ToList() ?? [];
        FleetQueue.ReplaceRange(levelingPlan?.Queue.Where(row => row.IsInFleet) ?? []);
        WaitingQueue.ReplaceRange(nearby.Where(row => !row.IsInFleet));
        RebuildQueue();
        DriverChoices.ReplaceRange(BuildDriverChoices());
        RebuildLevelingGear();
        OnPropertyChanged(nameof(LevelingPlan));
        OnPropertyChanged(nameof(DormPlan));
        OnPropertyChanged(nameof(HasLevelingShips));
        OnPropertyChanged(nameof(HasNoLevelingShips));
        OnPropertyChanged(nameof(HasWaitingQueue));
        OnPropertyChanged(nameof(MetaHintText));
        OnPropertyChanged(nameof(HasMetaHint));
    }

    private IEnumerable<DriverChoice> BuildDriverChoices()
    {
        var auto = levelingPlan?.AutoDriverCount ?? 0;
        yield return new DriverChoice(null, $"자동 {auto}", driverOverride is null);
        for (var count = 0; count <= LevelingPlanBuilder.MaxDrivers; count++)
            yield return new DriverChoice(count, count.ToString(CultureInfo.InvariantCulture), driverOverride == count);
    }

    private void SetDriverCount(DriverChoice? choice)
    {
        if (choice is null || choice.Count == driverOverride) return;
        driverOverride = choice.Count;
        RebuildLeveling();
        settingsChanged();
    }
}

public sealed class LevelingStageChoice(LevelingStageOption option)
{
    public LevelingStageOption Option { get; } = option;
    public string Key => Option.Stage.Key;
    public string Title => Option.Stage.DisplayTitle;
    public string Note => Option.Stage.Note;
    public bool HasNote => Note.Length > 0;
    public string EnemyLevelText => $"Lv.{Option.Stage.EnemyLevel}";
    public string ExpText => string.Create(CultureInfo.CurrentCulture,
        $"{Option.Stage.SRankExp:N0} / {Option.Stage.BonusExp:N0}");
    public string BasisText => Option.Stage.BasisText;
    public bool HasRecords => Option.Stage.HasRecords;
}

/// <summary>One button of the driver-count control; Count is null for the automatic choice.</summary>
public sealed record DriverChoice(int? Count, string Label, bool IsSelected);
