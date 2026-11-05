using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.ViewModels;

/// <summary>지휘냥: the fleet check, the owned cats under one filter, and the talent card of the chosen cat.</summary>
public sealed class MeowfficerViewModel : ObservableObject
{
    private MeowfficerDashboard dashboard = MeowfficerDashboard.Empty;
    private string filterKey = "all";
    private string selectedUid = "";

    public MeowfficerViewModel()
    {
        SelectFilterCommand = new RelayCommand(value => SelectFilter(value as MeowfficerFilter), value => value is MeowfficerFilter);
        SelectCatCommand = new RelayCommand(value => SelectCat(value as MeowfficerCopyRow), value => value is MeowfficerCopyRow);
    }

    public RelayCommand SelectFilterCommand { get; }
    public RelayCommand SelectCatCommand { get; }
    public BulkObservableCollection<MeowfficerFilter> Filters { get; } = [];
    public BulkObservableCollection<MeowfficerKindRow> VisibleKinds { get; } = [];

    public MeowfficerDashboard Dashboard => dashboard;
    public bool HasCats => dashboard.HasCats;
    public bool HasNoCats => !HasCats;
    public bool HasFleets => dashboard.Fleets.Count > 0;
    public string SelectedUid => selectedUid;
    public MeowfficerCopyRow? SelectedCat => dashboard.Copies.FirstOrDefault(copy => copy.Uid == selectedUid);
    public string CountText => $"{dashboard.Count}마리 · {VisibleKinds.Sum(kind => kind.Copies.Count)}마리 표시";

    public void ApplySnapshot(MeowfficerDashboard snapshot)
    {
        dashboard = snapshot;
        if (dashboard.Copies.All(copy => copy.Uid != selectedUid))
        {
            // Open on the first fleet's 지휘 cat, as the fleet check reads top to bottom; else the first cat listed.
            selectedUid = dashboard.Fleets.SelectMany(fleet => fleet.Slots).Select(slot => slot.Current?.Uid)
                              .FirstOrDefault(uid => uid is not null)
                          ?? dashboard.Copies.FirstOrDefault()?.Uid ?? "";
        }

        OnPropertyChanged(nameof(Dashboard));
        OnPropertyChanged(nameof(HasCats));
        OnPropertyChanged(nameof(HasNoCats));
        OnPropertyChanged(nameof(HasFleets));
        RebuildFilters();
        RebuildKinds();
        OnPropertyChanged(nameof(SelectedUid));
        OnPropertyChanged(nameof(SelectedCat));
    }

    private static readonly (string Key, string Label, Func<MeowfficerCopyRow, bool> Match)[] FilterRules =
    [
        ("all", "전체", _ => true),
        ("raise", "키울 만함", copy => copy.IsRaiseCandidate),
        ("max", "다 키움", copy => copy.IsMaxLevel),
        ("feed", "먹이 후보", copy => copy.Verdict == MeowfficerVerdict.Feed),
        ("fixed", "재능 고정", copy => copy.Fixed),
        ("none", "출처 평가 없음", copy => !copy.HasRole)
    ];

    private void RebuildFilters()
    {
        if (FilterRules.All(rule => rule.Key != filterKey)) filterKey = "all";
        Filters.ReplaceRange(FilterRules.Select(rule => new MeowfficerFilter(rule.Key, rule.Label,
            dashboard.Copies.Count(rule.Match), rule.Key == filterKey)));
    }

    private void RebuildKinds()
    {
        var match = FilterRules.First(rule => rule.Key == filterKey).Match;
        VisibleKinds.ReplaceRange(dashboard.Kinds
            .Select(kind => new MeowfficerKindRow
            {
                TemplateId = kind.TemplateId, Name = kind.Name, Rarity = kind.Rarity, RoleText = kind.RoleText,
                HasRole = kind.HasRole, Copies = kind.Copies.Where(match).ToList()
            })
            .Where(kind => kind.Copies.Count > 0));
        OnPropertyChanged(nameof(CountText));
    }

    private void SelectFilter(MeowfficerFilter? filter)
    {
        if (filter is null || filter.Key == filterKey) return;
        filterKey = filter.Key;
        RebuildFilters();
        RebuildKinds();
    }

    private void SelectCat(MeowfficerCopyRow? cat)
    {
        if (cat is null || cat.Uid == selectedUid) return;
        selectedUid = cat.Uid;
        OnPropertyChanged(nameof(SelectedUid));
        OnPropertyChanged(nameof(SelectedCat));
    }
}

public sealed record MeowfficerFilter(string Key, string Label, int Count, bool IsSelected)
{
    public string Text => $"{Label} {Count}";
}
