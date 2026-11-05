using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.App.ViewModels;

public sealed class GrowthPlannerViewModel : ObservableObject
{
    private readonly List<ShipGrowthGoal> goals;
    private readonly Action settingsChanged;
    private readonly Dictionary<string, ShipRow> shipsByUid = new(StringComparer.Ordinal);
    private readonly HashSet<string> trainingShips = new(StringComparer.Ordinal);
    private JsonElement levelTable;
    private string databasePath = "";
    private string search = "";
    private string targetLevelText = "120";
    private string targetSkillLevelText = "10";
    private bool trackLevel = true;
    private bool trackSkills = true;
    private bool refreshing;
    private ShipRow? selectedShip;
    private string feedback = "";
    private bool isEditorExpanded = true;
    public bool IsEditorExpanded { get => isEditorExpanded; set => SetProperty(ref isEditorExpanded, value); }

    /// <summary>Raised after the player adds, changes or removes a goal, so the 쩔 대기열 can reorder.</summary>
    public event EventHandler? GoalsChanged;

    public GrowthPlannerViewModel(IEnumerable<ShipGrowthGoal>? savedGoals, Action settingsChanged,
        Action<ShipRow> openShip)
    {
        goals = (savedGoals ?? []).Where(goal => goal is not null &&
            !string.IsNullOrWhiteSpace(goal.ShipUid)).ToList();
        this.settingsChanged = settingsChanged;
        ShipChoices = new ListCollectionView(AvailableShips);
        ShipChoices.Filter = item => item is ShipRow ship &&
            (string.IsNullOrWhiteSpace(Search) || ship.SearchText.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase));
        SaveGoalCommand = new RelayCommand(_ => SaveGoal(), _ => CanSave);
        RemoveGoalCommand = new RelayCommand(RemoveGoal);
        EditGoalCommand = new RelayCommand(EditGoal);
        OpenShipCommand = new RelayCommand(value =>
        {
            if (value is ShipGrowthPlan { Ship: { } ship }) openShip(ship);
        }, value => value is ShipGrowthPlan { Ship: not null });
    }

    public BulkObservableCollection<ShipRow> AvailableShips { get; } = [];
    public ICollectionView ShipChoices { get; }
    public BulkObservableCollection<ShipGrowthPlan> Plans { get; } = [];
    public IReadOnlyList<ShipGrowthGoal> SavedGoals => goals.ToArray();
    public IEnumerable<ShipGrowthPlan> PreviewPlans => Plans.Take(3);
    public RelayCommand SaveGoalCommand { get; }
    public RelayCommand RemoveGoalCommand { get; }
    public RelayCommand EditGoalCommand { get; }
    public RelayCommand OpenShipCommand { get; }

    public string Search
    {
        get => search;
        set { if (SetProperty(ref search, value ?? "")) ShipChoices.Refresh(); }
    }
    public ShipRow? SelectedShip
    {
        get => selectedShip;
        set
        {
            if (refreshing || !SetProperty(ref selectedShip, value)) return;
            var existing = goals.FirstOrDefault(goal => IsCurrentSource(goal) && goal.ShipUid == value?.ShipUid);
            if (existing is not null) LoadEditor(existing);
            else EditorChanged();
        }
    }
    public string TargetLevelText
    {
        get => targetLevelText;
        set { if (SetProperty(ref targetLevelText, value ?? "")) EditorChanged(); }
    }
    public string TargetSkillLevelText
    {
        get => targetSkillLevelText;
        set { if (SetProperty(ref targetSkillLevelText, value ?? "")) EditorChanged(); }
    }
    public bool TrackLevel
    {
        get => trackLevel;
        set { if (SetProperty(ref trackLevel, value)) EditorChanged(); }
    }
    public bool TrackSkills
    {
        get => trackSkills;
        set { if (SetProperty(ref trackSkills, value)) EditorChanged(); }
    }
    public string Feedback { get => feedback; private set => SetProperty(ref feedback, value); }
    public bool HasShips => AvailableShips.Count > 0;
    public bool HasPlans => Plans.Count > 0;
    public bool IsEmpty => !HasPlans;
    public string Summary => $"목표 {Plans.Count}척 · 진행 {Plans.Count(plan => !plan.IsComplete && !plan.NeedsData)} · " +
        $"달성 {Plans.Count(plan => plan.IsComplete)} · 정보 확인 {Plans.Count(plan => plan.NeedsData)}";
    public string ValidationMessage => !HasShips ? "도크 데이터가 없습니다. 게임에서 도크를 열고 새로고침해 주세요."
        : SelectedShip is null ? "먼저 육성할 함선을 선택하세요."
        : !TrackLevel && !TrackSkills ? "레벨 또는 스킬 목표를 하나 이상 선택하세요."
        : TrackLevel && !ValidNumber(TargetLevelText, 125) ? "목표 레벨은 1~125 사이의 정수로 입력하세요."
        : TrackSkills && !ValidNumber(TargetSkillLevelText, 10) ? "목표 스킬 레벨은 1~10 사이의 정수로 입력하세요."
        : "같은 함선의 목표를 다시 저장하면 기존 목표가 바뀝니다.";
    public bool CanSave => !string.IsNullOrWhiteSpace(databasePath) && SelectedShip is not null &&
        (TrackLevel || TrackSkills) && (!TrackLevel || ValidNumber(TargetLevelText, 125)) &&
        (!TrackSkills || ValidNumber(TargetSkillLevelText, 10));

    public void ApplySnapshot(DashboardSnapshot snapshot)
    {
        var newPath = NormalizePath(snapshot.DatabasePath);
        var sourceChanged = !StringComparer.OrdinalIgnoreCase.Equals(databasePath, newPath);
        var selectedUid = sourceChanged ? null : SelectedShip?.ShipUid;
        databasePath = newPath;
        levelTable = snapshot.ShipLevelTable;
        shipsByUid.Clear();
        foreach (var ship in snapshot.Ships) shipsByUid[ship.ShipUid] = ship;
        trainingShips.Clear();
        foreach (var room in snapshot.AcademyRooms) trainingShips.Add(room.ShipUid);
        // Retain the editor's in-progress values across automatic snapshot refreshes.
        refreshing = true;
        try
        {
            AvailableShips.ReplaceRange(snapshot.Ships.Where(ship => !ship.IsBulin)
                .OrderBy(ship => ship.DisplayName, StringComparer.CurrentCulture).ThenBy(ship => ship.ShipUid));
            selectedShip = selectedUid is null ? null : AvailableShips.FirstOrDefault(ship => ship.ShipUid == selectedUid);
        }
        finally { refreshing = false; }
        OnPropertyChanged(nameof(SelectedShip));
        OnPropertyChanged(nameof(HasShips));
        if (sourceChanged)
        {
            Feedback = "";
            IsEditorExpanded = !goals.Any(IsCurrentSource);
        }
        RefreshPlans();
        EditorChanged();
    }

    private void SaveGoal()
    {
        if (!CanSave || SelectedShip is null) return;
        var goal = new ShipGrowthGoal
        {
            DatabasePath = databasePath, ShipUid = SelectedShip.ShipUid, ShipName = SelectedShip.DisplayName,
            TrackLevel = TrackLevel, TargetLevel = TrackLevel ? int.Parse(TargetLevelText, CultureInfo.CurrentCulture) : 120,
            TrackSkills = TrackSkills, TargetSkillLevel = TrackSkills ? int.Parse(TargetSkillLevelText, CultureInfo.CurrentCulture) : 10
        };
        goals.RemoveAll(item => IsCurrentSource(item) && item.ShipUid == goal.ShipUid);
        goals.Add(goal);
        CommitGoals();
        Feedback = $"{goal.ShipName} 목표를 반영했습니다.";
        IsEditorExpanded = false;
    }

    /// <summary>Adds or raises a level goal from another page without touching the editor's inputs.</summary>
    public string AddLevelGoal(string shipUid, int targetLevel)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            return "데이터를 불러온 뒤 목표를 추가할 수 있습니다.";
        if (!shipsByUid.TryGetValue(shipUid, out var ship))
            return "함선 정보를 찾지 못했습니다. 새로고침한 뒤 다시 시도하세요.";

        var existing = goals.FirstOrDefault(goal => IsCurrentSource(goal) && goal.ShipUid == shipUid);
        var level = Math.Clamp(Math.Max(existing is { TrackLevel: true } ? existing.TargetLevel : 0, targetLevel), 1,
            ShipExperienceCalculator.MaximumLevel);
        var goal = (existing ?? new ShipGrowthGoal { DatabasePath = databasePath, ShipUid = shipUid })
            with { ShipName = ship.DisplayName, TrackLevel = true, TargetLevel = level };
        if (existing is not null) goals.Remove(existing);
        goals.Add(goal);
        CommitGoals();
        return existing is null
            ? $"{ship.DisplayName} 목표(Lv.{level})를 육성 계획에 추가했습니다."
            : $"{ship.DisplayName}의 육성 목표를 Lv.{level}로 맞췄습니다.";
    }

    /// <summary>Unpins a ship from the queue: drops its level goal but keeps a skill goal it also has.</summary>
    public string ReleaseLevelGoal(string shipUid)
    {
        var existing = goals.FirstOrDefault(goal => IsCurrentSource(goal) && goal.ShipUid == shipUid && goal.TrackLevel);
        if (existing is null) return "";
        goals.Remove(existing);
        if (existing.TrackSkills) goals.Add(existing with { TrackLevel = false });
        CommitGoals();
        return existing.TrackSkills
            ? $"{existing.ShipName}의 레벨 목표를 풀었습니다. 스킬 목표는 남겨 두었습니다."
            : $"{existing.ShipName}의 육성 목표를 풀었습니다.";
    }

    private void CommitGoals()
    {
        RefreshPlans();
        settingsChanged();
        GoalsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveGoal(object? value)
    {
        if (value is not ShipGrowthPlan plan || !goals.Remove(plan.Goal)) return;
        CommitGoals();
        Feedback = $"{plan.ShipName}의 목표를 목록에서 지웠습니다.";
        if (!HasPlans) IsEditorExpanded = true;
    }

    private void EditGoal(object? value)
    {
        if (value is not ShipGrowthPlan plan) return;
        IsEditorExpanded = true;
        Search = "";
        SelectedShip = AvailableShips.FirstOrDefault(ship => ship.ShipUid == plan.Goal.ShipUid);
        LoadEditor(plan.Goal);
        Feedback = SelectedShip is null ? "함선 데이터를 다시 확인한 후 목표를 수정할 수 있습니다." : "목표를 수정한 뒤 저장하세요.";
    }

    private void LoadEditor(ShipGrowthGoal goal)
    {
        TrackLevel = goal.TrackLevel;
        TrackSkills = goal.TrackSkills;
        TargetLevelText = goal.TargetLevel.ToString(CultureInfo.CurrentCulture);
        TargetSkillLevelText = goal.TargetSkillLevel.ToString(CultureInfo.CurrentCulture);
        EditorChanged();
    }

    private void RefreshPlans()
    {
        Plans.ReplaceRange(goals.Where(IsCurrentSource)
            .Select(goal => ShipGrowthPlanner.Build(goal, shipsByUid.GetValueOrDefault(goal.ShipUid), levelTable,
                trainingShips.Contains(goal.ShipUid)))
            .OrderBy(plan => plan.IsComplete).ThenBy(plan => plan.NeedsData)
            .ThenBy(plan => plan.ShipName, StringComparer.CurrentCulture));
        OnPropertyChanged(nameof(HasPlans));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(PreviewPlans));
    }

    private void EditorChanged()
    {
        OnPropertyChanged(nameof(ValidationMessage));
        SaveGoalCommand.RaiseCanExecuteChanged();
    }
    /// <summary>Ships of the loaded database with a level goal, for the leveling planner's priority.</summary>
    public IReadOnlySet<string> LevelGoalShipUids() =>
        goals.Where(goal => goal.TrackLevel && IsCurrentSource(goal)).Select(goal => goal.ShipUid)
            .ToHashSet(StringComparer.Ordinal);

    private bool IsCurrentSource(ShipGrowthGoal goal) =>
        StringComparer.OrdinalIgnoreCase.Equals(NormalizePath(goal.DatabasePath), databasePath);
    private static bool ValidNumber(string text, int maximum) =>
        int.TryParse(text, out var number) && number >= 1 && number <= maximum;
    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return path; }
    }
}
