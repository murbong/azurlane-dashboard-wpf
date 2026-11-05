using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.App.ViewModels;

/// <summary>What the 장비 추천 panel shows for one ship of a planned fleet (META 보스전 or 쫄작).</summary>
public sealed class GearPanel
{
    public static GearPanel Empty { get; } = new();

    public string ShipUid { get; init; } = "";
    public string ShipName { get; init; } = "";
    public string ShipSubText { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public IReadOnlyList<GearSlotRow> Rows { get; init; } = [];
    public string FootText { get; init; } = "";
    public string ArmorNote { get; init; } = "";

    public bool HasShip => ShipUid.Length > 0;
    public bool HasRows => Rows.Count > 0;
    public bool HasNoRows => HasShip && Rows.Count == 0;

    /// <summary>
    /// Builds the panel for <paramref name="shipUid"/> from a fleet-wide allocation, so the rows already
    /// account for what the ship's fleet mates keep.
    /// </summary>
    public static GearPanel For(
        IReadOnlyList<GearShipPlan> plans, string shipUid, string name, string subText, string iconUrl, string rarity,
        FleetPlannerInputs inputs, GearSourceMode mode, string armorLabel = "", string alternateArmorLabel = "")
    {
        var plan = plans.FirstOrDefault(item => item.Ship.Uid == shipUid);
        return new GearPanel
        {
            ShipUid = shipUid,
            ShipName = name,
            ShipSubText = subText,
            IconUrl = iconUrl,
            Rarity = rarity,
            Rows = plan is null
                ? []
                : GearPlanPresenter.Rows(plan, inputs.GearItems, inputs.GearInventory, mode, alternateArmorLabel,
                    inputs.AugmentItems),
            FootText = plan is null ? "" : GearPlanPresenter.FootText(plan, mode),
            ArmorNote = plan is null || alternateArmorLabel.Length == 0
                ? ""
                : GearPlanPresenter.ArmorNote(plan, armorLabel, alternateArmorLabel)
        };
    }
}
