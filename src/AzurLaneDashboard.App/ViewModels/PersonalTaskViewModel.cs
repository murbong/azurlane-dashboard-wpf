using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.ViewModels;

public sealed record PersonalTaskFilter(string Key, string Text, bool IsSelected);

/// <summary>캐릭터 임무: every character chain under one status filter.</summary>
public sealed class PersonalTaskViewModel : ObservableObject
{
    private PersonalTaskDashboard dashboard = PersonalTaskDashboard.Empty;
    private string filterKey = PersonalTaskStatus.Active;

    public PersonalTaskViewModel()
    {
        SelectFilterCommand = new RelayCommand(value => SelectFilter(value as PersonalTaskFilter), value => value is PersonalTaskFilter);
    }

    public RelayCommand SelectFilterCommand { get; }
    public BulkObservableCollection<PersonalTaskFilter> Filters { get; } = [];
    public BulkObservableCollection<PersonalTaskRow> VisibleRows { get; } = [];
    public PersonalTaskDashboard Dashboard => dashboard;
    public bool HasData => dashboard.HasData;
    public bool HasNoData => !HasData;
    public bool HasNoVisibleRows => HasData && VisibleRows.Count == 0;

    public void ApplySnapshot(PersonalTaskDashboard snapshot)
    {
        dashboard = snapshot;
        // Open on the chains in progress; with none, on the ones ready to start.
        if (filterKey == PersonalTaskStatus.Active && dashboard.ActiveCount == 0 && dashboard.ReadyCount > 0)
            filterKey = PersonalTaskStatus.Ready;
        Rebuild();
        OnPropertyChanged(string.Empty);
    }

    private void SelectFilter(PersonalTaskFilter? filter)
    {
        if (filter is null || filter.Key == filterKey) return;
        filterKey = filter.Key;
        Rebuild();
        OnPropertyChanged(nameof(HasNoVisibleRows));
    }

    private void Rebuild()
    {
        Filters.ReplaceRange(new[]
        {
            (PersonalTaskStatus.Active, $"진행 중 {dashboard.ActiveCount}"),
            (PersonalTaskStatus.Ready, $"시작 가능 {dashboard.ReadyCount}"),
            (PersonalTaskStatus.Done, $"완료 {dashboard.DoneCount}"),
            (PersonalTaskStatus.Locked, $"미보유 {dashboard.LockedCount}"),
            ("all", $"전체 {dashboard.Rows.Count}")
        }.Select(item => new PersonalTaskFilter(item.Item1, item.Item2, item.Item1 == filterKey)));
        VisibleRows.ReplaceRange(dashboard.Rows.Where(row => filterKey == "all" || row.Status == filterKey));
    }
}
