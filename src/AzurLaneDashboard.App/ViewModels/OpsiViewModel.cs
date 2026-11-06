using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.App.ViewModels;

/// <summary>대형작전: stars left by action or by sea area, claimable rewards, device items and adaptability.</summary>
public sealed class OpsiViewModel : ObservableObject
{
    private OpsiDashboard dashboard = OpsiDashboard.Empty;
    private bool byArea;

    public OpsiViewModel()
    {
        SetViewCommand = new RelayCommand(value => SetView(value as string));
    }

    public RelayCommand SetViewCommand { get; }
    public OpsiDashboard Dashboard => dashboard;
    public bool HasData => dashboard.HasData;
    public bool HasNoData => !HasData;
    public bool IsActionView => !byArea;
    public bool IsAreaView => byArea;
    public string ListTitle => byArea
        ? $"남은 별 {dashboard.StarsLeft}개 · 해역별"
        : $"남은 별 {dashboard.StarsLeft}개 · 할 일별";

    public void ApplySnapshot(OpsiDashboard snapshot)
    {
        dashboard = snapshot;
        OnPropertyChanged(string.Empty);
    }

    private void SetView(string? key)
    {
        var area = key == "area";
        if (area == byArea) return;
        byArea = area;
        OnPropertyChanged(nameof(IsActionView));
        OnPropertyChanged(nameof(IsAreaView));
        OnPropertyChanged(nameof(ListTitle));
    }
}
