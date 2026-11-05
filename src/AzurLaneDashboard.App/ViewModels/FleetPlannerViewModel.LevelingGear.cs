using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class FleetPlannerViewModel
{
    private string selectedLevelingShipUid = "";
    private IReadOnlyList<LevelingSlot?> levelingMainSlots = [];
    private IReadOnlyList<LevelingSlot?> levelingVanguardSlots = [];
    private GearPanel levelingGearPanel = GearPanel.Empty;
    private string levelingGearUsecase = "";
    private RelayCommand? selectLevelingShipCommand;

    /// <summary>The leveling fleet's tiles, carrying how many gear slots the recommendation would change.</summary>
    public IReadOnlyList<LevelingSlot?> LevelingMainSlots => levelingMainSlots;
    public IReadOnlyList<LevelingSlot?> LevelingVanguardSlots => levelingVanguardSlots;
    /// <summary>The 장비 추천 card for the selected leveling fleet ship.</summary>
    public GearPanel LevelingGearPanel => levelingGearPanel;
    public string SelectedLevelingShipUid => selectedLevelingShipUid;
    public string LevelingGearBasisText => levelingGearUsecase.Length == 0
        ? "Regal 해역 순위 · 보유 장비로 대체"
        : $"Regal 해역 순위({(levelingGearUsecase == "C-13+" ? "13장 이상" : "12장 이하")}) · 보유 장비로 대체";

    public RelayCommand SelectLevelingShipCommand => selectLevelingShipCommand ??=
        new RelayCommand(value => SelectLevelingShip(value as LevelingSlot), value => value is LevelingSlot);

    /// <summary>
    /// Allocates chapter gear across the whole leveling fleet (drivers first, as they carry the stage) and
    /// opens the selected ship's card: the first driver by default, else the flagship.
    /// </summary>
    private void RebuildLevelingGear()
    {
        var inputs = plan.Inputs;
        var changes = new Dictionary<string, int>(StringComparer.Ordinal);
        levelingGearPanel = GearPanel.Empty;
        levelingGearUsecase = "";
        IReadOnlyList<LevelingSlot> order = levelingPlan is null ? [] : LevelingPlanBuilder.GearOrder(levelingPlan);
        if (order.All(slot => slot.ShipUid != selectedLevelingShipUid))
            selectedLevelingShipUid = order.FirstOrDefault()?.ShipUid ?? "";

        if (levelingPlan is not null && selectedStage is not null && inputs.GearMap is not null)
        {
            levelingGearUsecase = LevelingPlanBuilder.GearUsecase(selectedStage.Option.Stage);
            var fleet = order
                .Where(slot => inputs.GearShips.ContainsKey(slot.ShipUid))
                .Select(slot => inputs.GearShips[slot.ShipUid] with { IsFlagship = slot.IsFlagship })
                .ToList();
            var plans = GearAllocator.Allocate(fleet, inputs.GearMap, levelingGearUsecase, inputs.GearInventory, gearMode);
            foreach (var shipPlan in plans) changes[shipPlan.Ship.Uid] = shipPlan.ChangeCount;
            var selected = order.FirstOrDefault(slot => slot.ShipUid == selectedLevelingShipUid);
            if (selected is not null)
            {
                levelingGearPanel = GearPanel.For(plans, selected.ShipUid, selected.Name,
                    $"{selected.RoleText} · {selected.SubText}", selected.IconUrl, selected.Rarity, inputs, gearMode);
            }
        }

        levelingMainSlots = WithGearChanges(levelingPlan?.MainSlots, changes);
        levelingVanguardSlots = WithGearChanges(levelingPlan?.VanguardSlots, changes);
        OnPropertyChanged(nameof(LevelingMainSlots));
        OnPropertyChanged(nameof(LevelingVanguardSlots));
        OnPropertyChanged(nameof(SelectedLevelingShipUid));
        OnPropertyChanged(nameof(LevelingGearPanel));
        OnPropertyChanged(nameof(LevelingGearBasisText));
        OnPropertyChanged(nameof(IsGearMoveMode));
        OnPropertyChanged(nameof(IsGearBagMode));
    }

    private static IReadOnlyList<LevelingSlot?> WithGearChanges(
        IReadOnlyList<LevelingSlot?>? slots, IReadOnlyDictionary<string, int> changes) =>
        slots?.Select(slot => slot?.WithGearChanges(changes.GetValueOrDefault(slot.ShipUid))).ToList() ?? [];

    private void SelectLevelingShip(LevelingSlot? slot)
    {
        if (slot is null || slot.ShipUid == selectedLevelingShipUid) return;
        selectedLevelingShipUid = slot.ShipUid;
        RebuildLevelingGear();
    }
}
