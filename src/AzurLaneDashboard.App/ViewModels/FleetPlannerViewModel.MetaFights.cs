using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.App.ViewModels;

/// <summary>실전 기록: recorded fights against the selected META boss and what stands out in each.</summary>
public sealed partial class FleetPlannerViewModel
{
    private MetaFightLog metaFightLog = MetaFightLog.Empty;
    private string selectedMetaFightKey = "";

    public RelayCommand SelectMetaFightCommand { get; }

    public MetaFightLog MetaFightLog => metaFightLog;
    public bool HasMetaFights => metaFightLog.HasFights;
    public bool HasNoMetaFights => !HasMetaFights;
    public string SelectedMetaFightKey => selectedMetaFightKey;
    public MetaFightRow? SelectedMetaFight => metaFightLog.Fights.FirstOrDefault(fight => fight.Key == selectedMetaFightKey);
    public string MetaFightEmptyText => plan.Inputs.MetaFights.Count == 0
        ? "아직 기록된 META전이 없습니다. 캡처를 켠 채 META전을 한 번 치면 피해량과 그 편성이 여기에 쌓입니다."
        : $"{selectedBoss?.Name ?? "이 보스"} 전투 기록이 없습니다. 다른 보스를 고르면 그 보스의 기록이 보입니다.";

    private void RebuildMetaFights()
    {
        var boss = selectedBoss?.Boss;
        var inputs = plan.Inputs;
        metaFightLog = boss is null
            ? MetaFightLog.Empty
            : MetaBossFightAnalyzer.Build(boss, inputs.MetaBossGuides.GetValueOrDefault(boss.Id), inputs.MetaFights,
                inputs.MetaFightShips, metaPlan, LatestFightGear(boss));
        if (metaFightLog.Fights.All(fight => fight.Key != selectedMetaFightKey))
            selectedMetaFightKey = metaFightLog.Fights.FirstOrDefault()?.Key ?? "";
        OnPropertyChanged(nameof(MetaFightLog));
        OnPropertyChanged(nameof(HasMetaFights));
        OnPropertyChanged(nameof(HasNoMetaFights));
        OnPropertyChanged(nameof(MetaFightEmptyText));
        OnPropertyChanged(nameof(SelectedMetaFightKey));
        OnPropertyChanged(nameof(SelectedMetaFight));
        OnPropertyChanged(nameof(HasLastMetaSortie));
        OnPropertyChanged(nameof(MetaOverlapText));
        OnPropertyChanged(nameof(IsMetaOverlapFull));
        OnPropertyChanged(nameof(MetaOverlapToolTip));
    }

    // Regal's gear for the latest fight's ships as they are equipped now, allocated as one fleet from the flagship.
    private IReadOnlyList<GearShipPlan>? LatestFightGear(MetaBoss boss)
    {
        var inputs = plan.Inputs;
        if (inputs.GearMap is null || metaPlan is null) return null;
        var latest = MetaBossFightAnalyzer.Build(boss, null, inputs.MetaFights, inputs.MetaFightShips).Fights.FirstOrDefault();
        if (latest is null) return null;
        var fleet = latest.MainShips.Concat(latest.VanguardShips)
            .Where(ship => inputs.GearShips.ContainsKey(ship.Uid))
            .Select(ship => inputs.GearShips[ship.Uid] with { IsFlagship = ship.Role == "기함" })
            .ToList();
        return fleet.Count == 0
            ? null
            : GearAllocator.Allocate(fleet, inputs.GearMap, metaPlan.GearUsecase, inputs.GearInventory, gearMode);
    }

    private void SelectMetaFight(MetaFightRow? fight)
    {
        if (fight is null || fight.Key == selectedMetaFightKey) return;
        selectedMetaFightKey = fight.Key;
        OnPropertyChanged(nameof(SelectedMetaFightKey));
        OnPropertyChanged(nameof(SelectedMetaFight));
    }

    // The META fleet is saved on the device only and rebuilt per boss; this boss's last captured fight stands in for it.
    private int LastSortieOverlap() => metaPlan is null
        ? 0
        : metaPlan.Slots.Count(slot => metaFightLog.LatestFleetUids.Contains(slot.ShipUid, StringComparer.Ordinal));
}
