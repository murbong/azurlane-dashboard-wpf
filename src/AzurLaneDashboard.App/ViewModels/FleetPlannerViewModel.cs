using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class FleetPlannerViewModel : ObservableObject
{
    public const string LevelingKey = "leveling";
    public const string MetaKey = "meta";

    private readonly GrowthPlannerViewModel growthPlanner;
    private readonly Action settingsChanged;
    private FleetPlanDashboard plan = new();
    private string selectedKey = FleetPlanBuilder.Contents[0].Key;
    private string highlightedShipUid = "";
    private string feedback = "";

    public FleetPlannerViewModel(GrowthPlannerViewModel growthPlanner, Action<string> openShip,
        AppSettings? settings = null, Action? settingsChanged = null)
    {
        this.growthPlanner = growthPlanner;
        this.settingsChanged = settingsChanged ?? (() => { });
        AddGoalCommand = new RelayCommand(AddGoal, value => ShipUid(value).Length > 0);
        OpenShipCommand = new RelayCommand(value => openShip(ShipUid(value)), value => ShipUid(value).Length > 0);
        SetDriverCountCommand = new RelayCommand(value => SetDriverCount(value as DriverChoice));
        SelectMetaShipCommand = new RelayCommand(value => SelectMetaShip(value as MetaBossSlot), value => value is MetaBossSlot);
        SetGearModeCommand = new RelayCommand(value => SetGearMode(value as string));
        SelectMetaFightCommand = new RelayCommand(value => SelectMetaFight(value as MetaFightRow), value => value is MetaFightRow);
        driverOverride = settings?.LevelingDriverCount;
        selectedStageKey = settings?.LevelingStageKey ?? "";
        savedBossId = settings?.MetaBossTemplateId ?? 0;
        gearMode = settings?.GearSourceMode == "bag" ? GearSourceMode.Bag : GearSourceMode.Move;
        RebuildTabs();
        InitializeQueue();
    }

    public BulkObservableCollection<FleetPlanTab> ChapterTabs { get; } = [];
    public BulkObservableCollection<FleetPlanTab> BossTabs { get; } = [];
    public BulkObservableCollection<FleetPlanTab> LevelingTabs { get; } = [];
    public RelayCommand AddGoalCommand { get; }
    public RelayCommand OpenShipCommand { get; }

    public FleetPlanContent? SelectedContent =>
        plan.Contents.FirstOrDefault(content => content.Key == selectedKey) ?? plan.Contents.FirstOrDefault();
    public bool IsLevelingSelected => selectedKey == LevelingKey;
    public bool IsMetaSelected => EffectiveKey == MetaKey;
    public bool IsStandardSelected => !IsLevelingSelected && !IsMetaSelected;
    public IReadOnlyList<CommunityRatingChangeRow> RatingChanges => plan.RatingChanges;
    public bool HasRatingChanges => plan.HasRatingChanges;
    public bool HasPlan => plan.HasContents;
    public bool IsEmpty => !HasPlan;
    public bool ShowsStandardBody => HasPlan && IsStandardSelected;
    public bool ShowsLevelingBody => HasPlan && IsLevelingSelected;
    public bool ShowsMetaBody => HasPlan && IsMetaSelected;
    public string RatingSourceText => string.IsNullOrWhiteSpace(plan.RatingSourceText) ? "-" : plan.RatingSourceText;
    public string RosterText => plan.RosterText;
    public string Feedback { get => feedback; private set => SetProperty(ref feedback, value); }

    /// <summary>The ready ship a hovered candidate would replace, so its slot can be outlined.</summary>
    public string HighlightedShipUid
    {
        get => highlightedShipUid;
        set => SetProperty(ref highlightedShipUid, value ?? "");
    }

    private string EffectiveKey => selectedKey == LevelingKey || plan.Contents.Count == 0 ||
                                   plan.Contents.Any(content => content.Key == selectedKey)
        ? selectedKey
        : plan.Contents[0].Key;

    public void ApplySnapshot(DashboardSnapshot snapshot)
    {
        plan = snapshot.FleetPlan;
        shipLevelCaps = new ShipLevelCaps(snapshot.ShipLevelTable);
        HighlightedShipUid = "";
        RefreshTabs();
        OnPropertyChanged(nameof(SelectedContent));
        OnPropertyChanged(nameof(RatingChanges));
        OnPropertyChanged(nameof(HasRatingChanges));
        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(RatingSourceText));
        OnPropertyChanged(nameof(RosterText));
        RebuildStageChoices();
        RebuildLeveling();
        RebuildBossChoices();
        RebuildMeta();
    }

    internal void Select(string key)
    {
        if (selectedKey == key) return;
        selectedKey = key;
        Feedback = "";
        HighlightedShipUid = "";
        RefreshTabs();
        OnPropertyChanged(nameof(SelectedContent));
    }

    internal bool IsSelected(string key) => EffectiveKey == key;

    private void RefreshTabs()
    {
        foreach (var tab in ChapterTabs.Concat(BossTabs).Concat(LevelingTabs)) tab.Refresh();
        OnPropertyChanged(nameof(IsLevelingSelected));
        OnPropertyChanged(nameof(IsMetaSelected));
        OnPropertyChanged(nameof(IsStandardSelected));
        OnPropertyChanged(nameof(ShowsStandardBody));
        OnPropertyChanged(nameof(ShowsLevelingBody));
        OnPropertyChanged(nameof(ShowsMetaBody));
    }

    private void RebuildTabs()
    {
        // The content list is fixed, so tabs are built once and survive refreshes with their focus.
        var tabs = FleetPlanBuilder.Contents.Select(content => new FleetPlanTab(this, content.Key, content.Label, content.Group)).ToList();
        ChapterTabs.ReplaceRange(tabs.Where(tab => tab.Group == "해역"));
        BossTabs.ReplaceRange(tabs.Where(tab => tab.Group != "해역"));
        LevelingTabs.ReplaceRange([new FleetPlanTab(this, LevelingKey, "쫄작", "레벨링")]);
    }

    private void AddGoal(object? value)
    {
        var (uid, level) = value switch
        {
            FleetPlanCandidate candidate => (candidate.ShipUid, candidate.TargetLevel),
            FleetPlanFinishItem item => (item.ShipUid, ShipExperienceCalculator.MaximumLevel),
            LevelingQueueRow row => (row.ShipUid, LevelingPlanBuilder.ReadyLevel),
            _ => ("", 0)
        };
        if (uid.Length == 0) return;
        Feedback = growthPlanner.AddLevelGoal(uid, level);
    }

    private static string ShipUid(object? value) => value switch
    {
        string uid => uid,
        FleetPlanCandidate candidate => candidate.ShipUid,
        FleetPlanFinishItem item => item.ShipUid,
        FleetPlanSlot slot => slot.ShipUid,
        LevelingSlot slot => slot.ShipUid,
        LevelingQueueRow row => row.ShipUid,
        MetaBossSlot slot => slot.ShipUid,
        _ => ""
    };
}

public sealed class FleetPlanTab(FleetPlannerViewModel owner, string key, string label, string group) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Group { get; } = group;

    public bool IsSelected
    {
        get => owner.IsSelected(Key);
        set { if (value) owner.Select(Key); }
    }

    internal void Refresh() => OnPropertyChanged(nameof(IsSelected));
}
