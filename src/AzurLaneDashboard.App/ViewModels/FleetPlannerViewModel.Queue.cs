using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.App.ViewModels;

/// <summary>
/// The 쩔 대기열 is the one growth order of the app: the 육성 계획 page lists all of it, the home card its top,
/// the 쫄작 tab the part next to the fleet, and content cards show where their candidates stand in it.
/// </summary>
public sealed partial class FleetPlannerViewModel
{
    public const int HomeQueueLength = 5;

    private Func<string, bool> queueShipFilter = _ => true;
    private string queueSegment = QueueSegmentKeys.All;
    private bool showUnfarmable = true;
    private GrowthQueueIndex queueIndex = GrowthQueueIndex.Empty;

    public BulkObservableCollection<LevelingQueueRow> GrowthQueue { get; } = [];
    public ICollectionView GrowthQueueView { get; private set; } = null!;
    public BulkObservableCollection<LevelingQueueRow> HomeQueue { get; } = [];
    public BulkObservableCollection<QueueSegment> QueueSegments { get; } = [];
    public RelayCommand SetQueueSegmentCommand { get; private set; } = null!;
    public RelayCommand TogglePinCommand { get; private set; } = null!;

    /// <summary>Where each ship stands in the queue, for cards outside it.</summary>
    public GrowthQueueIndex QueueIndex => queueIndex;
    public bool HasGrowthQueue => GrowthQueue.Count > 0;
    public bool HasNoGrowthQueue => !HasGrowthQueue;
    public string QueueStageTitle => levelingPlan?.Stage.DisplayTitle ?? "";
    public string QueueStageDetail => levelingPlan is null
        ? ""
        : string.Create(CultureInfo.CurrentCulture,
            $"기준 · 1전 {levelingPlan.Stage.SRankExp:N0} EXP · 전의 보너스 {levelingPlan.Stage.BonusExp:N0}");
    public string QueuePoolText => levelingPlan is null
        ? ""
        : string.Create(CultureInfo.CurrentCulture, $"잠금 {levelingPlan.PoolCount:N0}척 · 잠수함 제외");
    public string QueueShowAllText => string.Create(CultureInfo.CurrentCulture, $"전체 {GrowthQueue.Count:N0}척 보기");

    public bool ShowUnfarmable
    {
        get => showUnfarmable;
        set
        {
            if (!SetProperty(ref showUnfarmable, value)) return;
            GrowthQueueView.Refresh();
        }
    }

    private void InitializeQueue()
    {
        GrowthQueueView = CollectionViewSource.GetDefaultView(GrowthQueue);
        GrowthQueueView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LevelingQueueRow.GroupTitle)));
        GrowthQueueView.Filter = item => item is LevelingQueueRow row && ShowsInQueue(row);
        SetQueueSegmentCommand = new RelayCommand(value => SetQueueSegment((value as QueueSegment)?.Key));
        TogglePinCommand = new RelayCommand(TogglePin, value => ShipUid(value).Length > 0);
        growthPlanner.GoalsChanged += (_, _) => RebuildLeveling();
    }

    /// <summary>The page's shared search and ship filters, applied to the ship behind each row.</summary>
    internal void SetQueueShipFilter(Func<string, bool> filter)
    {
        queueShipFilter = filter;
    }

    internal void RefreshQueueFilter() => GrowthQueueView.Refresh();

    private void RebuildQueue()
    {
        var rows = levelingPlan?.Queue ?? [];
        GrowthQueue.ReplaceRange(rows);
        HomeQueue.ReplaceRange(rows.Take(HomeQueueLength));
        queueIndex = new GrowthQueueIndex(rows);
        QueueSegments.ReplaceRange(
        [
            new QueueSegment(QueueSegmentKeys.All, "전체", rows.Count, queueSegment == QueueSegmentKeys.All),
            new QueueSegment(QueueSegmentKeys.Goals, "육성 목표", rows.Count(IsGoalRow), queueSegment == QueueSegmentKeys.Goals),
            new QueueSegment(QueueSegmentKeys.Content, "콘텐츠 후보", rows.Count(IsContentRow), queueSegment == QueueSegmentKeys.Content),
            new QueueSegment(QueueSegmentKeys.Farm, "쫄작 중", rows.Count(IsFarmRow), queueSegment == QueueSegmentKeys.Farm)
        ]);
        OnPropertyChanged(nameof(QueueIndex));
        OnPropertyChanged(nameof(HasGrowthQueue));
        OnPropertyChanged(nameof(HasNoGrowthQueue));
        OnPropertyChanged(nameof(QueueStageTitle));
        OnPropertyChanged(nameof(QueueStageDetail));
        OnPropertyChanged(nameof(QueuePoolText));
        OnPropertyChanged(nameof(QueueShowAllText));
    }

    private bool ShowsInQueue(LevelingQueueRow row)
    {
        if (!showUnfarmable && row.StatusKind == "out") return false;
        var inSegment = queueSegment switch
        {
            QueueSegmentKeys.Goals => IsGoalRow(row),
            QueueSegmentKeys.Content => IsContentRow(row),
            QueueSegmentKeys.Farm => IsFarmRow(row),
            _ => true
        };
        return inSegment && queueShipFilter(row.ShipUid);
    }

    private static bool IsGoalRow(LevelingQueueRow row) => row.Ship.IsGrowthGoal;
    private static bool IsContentRow(LevelingQueueRow row) => row.Ship.ContentReach > 0 || row.Ship.IsMetaGuideShip;
    private static bool IsFarmRow(LevelingQueueRow row) => row.Ship.FarmFleetId > 0;

    private void SetQueueSegment(string? key)
    {
        if (key is null || key == queueSegment) return;
        queueSegment = key;
        foreach (var segment in QueueSegments) segment.IsSelected = segment.Key == key;
        GrowthQueueView.Refresh();
    }

    /// <summary>A pinned ship is a Lv.120 growth goal; unpinning keeps any skill goal the ship has.</summary>
    private void TogglePin(object? value)
    {
        var uid = ShipUid(value);
        if (uid.Length == 0) return;
        Feedback = growthPlanner.LevelGoalShipUids().Contains(uid)
            ? growthPlanner.ReleaseLevelGoal(uid)
            : growthPlanner.AddLevelGoal(uid, LevelingPlanBuilder.ReadyLevel);
    }
}

public static class QueueSegmentKeys
{
    public const string All = "all";
    public const string Goals = "goals";
    public const string Content = "content";
    public const string Farm = "farm";
}

public sealed class QueueSegment(string key, string label, int count, bool isSelected) : ObservableObject
{
    private bool isSelected = isSelected;

    public string Key { get; } = key;
    public string Label { get; } = label;
    public string CountText { get; } = count.ToString("N0", CultureInfo.CurrentCulture);

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}

/// <summary>Each ship's queue row, for cards that live outside the queue.</summary>
public sealed class GrowthQueueIndex(IReadOnlyList<LevelingQueueRow> rows)
{
    public static readonly GrowthQueueIndex Empty = new([]);

    private readonly Dictionary<string, LevelingQueueRow> byUid =
        rows.GroupBy(row => row.ShipUid, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(),
            StringComparer.Ordinal);

    public LevelingQueueRow? Row(string uid) => byUid.GetValueOrDefault(uid);
}
