namespace AzurLaneDashboard.Core.Models;

/// <summary>One copy of a gear piece: its base id (enhancement folded away) and enhancement.</summary>
public sealed record GearItemCopy(int BaseId, int Enhance);

/// <summary>A gear piece equipped on a ship outside the planned fleet, which may be moved over.</summary>
public sealed record GearHolder(string ShipUid, string ShipName, int Slot, int BaseId, int Enhance, int ShipLevel,
    bool InSortieFleet);

public sealed record GearFleetShip(
    string Uid,
    string Name,
    int RegalGearId,
    bool IsFlagship,
    IReadOnlyDictionary<int, GearItemCopy> Current,
    GearSlotRules? SlotRules = null,
    int RegalAugmentId = 0,
    GearItemCopy? CurrentAugment = null);

/// <summary>
/// The equipment types each gear slot takes at the ship's current template (limit-break stage), from
/// ship_data_template equip_1..equip_5. Regal ranks assume a fully limit-broken ship, so a low-break copy
/// can rank pieces it cannot equip yet.
/// </summary>
public sealed class GearSlotRules
{
    /// <summary>Slot 1..5 → allowed equipment types. An empty set means unknown (anything goes).</summary>
    public IReadOnlyList<IReadOnlySet<int>> Allowed { get; init; } = [];
    /// <summary>Slot 1..5 → type → the limit break that first allows it, for types later breaks add.</summary>
    public IReadOnlyList<IReadOnlyDictionary<int, int>> UnlockLimitBreak { get; init; } = [];
    /// <summary>Slot 1..5 → types only the ship's retrofit allows (Regal often ranks the retrofitted ship).</summary>
    public IReadOnlyList<IReadOnlySet<int>> UnlockByRetrofit { get; init; } = [];

    /// <summary>Unknown slot or type counts as a fit, so missing data never hides a recommendation.</summary>
    public bool Fits(int slot, int type) =>
        type <= 0 || slot < 1 || slot > Allowed.Count || Allowed[slot - 1].Count == 0 || Allowed[slot - 1].Contains(type);

    /// <summary>0 when no later break of this ship allows the type in that slot.</summary>
    public int UnlocksAt(int slot, int type) =>
        slot >= 1 && slot <= UnlockLimitBreak.Count ? UnlockLimitBreak[slot - 1].GetValueOrDefault(type) : 0;

    public bool UnlocksWithRetrofit(int slot, int type) =>
        slot >= 1 && slot <= UnlockByRetrofit.Count && UnlockByRetrofit[slot - 1].Contains(type);
}

public sealed class GearInventory
{
    /// <summary>Spare copies in the warehouse: base id → enhancement of each copy.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<int>> Spare { get; init; } = new Dictionary<int, IReadOnlyList<int>>();
    /// <summary>Every equipped piece in the dock; pieces on the planned fleet are filtered out by the allocator.</summary>
    public IReadOnlyList<GearHolder> Equipped { get; init; } = [];
    /// <summary>Equipment type (equip_data_statistics <c>type</c>) of each base id, checked against slot rules.</summary>
    public IReadOnlyDictionary<int, int> Types { get; init; } = new Dictionary<int, int>();
    /// <summary>
    /// Spare special equipment (augment modules): base id → enhancement of each copy. Their ids overlap with
    /// equipment ids, so they never share a table with <see cref="Spare"/>.
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<int>> AugmentSpare { get; init; } =
        new Dictionary<int, IReadOnlyList<int>>();
    /// <summary>Special equipment worn by ships, with <see cref="GearHolder.Slot"/> = <c>GearAllocator.AugmentSlot</c>.</summary>
    public IReadOnlyList<GearHolder> AugmentEquipped { get; init; } = [];
}

public enum GearSourceMode
{
    /// <summary>Warehouse spares and pieces on ships outside the fleet.</summary>
    Move,
    /// <summary>Warehouse spares only.</summary>
    Bag
}

public enum GearSlotAction
{
    /// <summary>The equipped piece is the best reachable choice.</summary>
    Keep,
    /// <summary>Take a spare from the warehouse.</summary>
    Bag,
    /// <summary>Take it off a ship outside the fleet.</summary>
    Move,
    /// <summary>The slot is empty and nothing ranked is reachable.</summary>
    Missing,
    /// <summary>The community data has no ranking for this ship.</summary>
    NoData
}

public sealed class GearSlotPlan
{
    public int Slot { get; init; }
    public GearSlotAction Action { get; init; }
    /// <summary>The piece to equip (for Keep, the equipped one); 0 when there is no ranking.</summary>
    public int BaseId { get; init; }
    /// <summary>1 = best row; 0 = not in the ranking at all.</summary>
    public int Rank { get; init; }
    public int Enhance { get; init; }
    public GearItemCopy? Current { get; init; }
    public int CurrentRank { get; init; }
    public GearHolder? MoveFrom { get; init; }
    /// <summary>A better-ranked piece the fleet already uses elsewhere, which is why this slot settles lower.</summary>
    public int BlockedBaseId { get; init; }
    public int BlockedRank { get; init; }
    public IReadOnlyList<string> BlockedBy { get; init; } = [];
    /// <summary>
    /// A better-ranked piece the player could get but this slot cannot take at the ship's current limit break.
    /// </summary>
    public int LockedBaseId { get; init; }
    public int LockedRank { get; init; }
    /// <summary>The limit break that allows <see cref="LockedBaseId"/>; 0 when no break of this ship does.</summary>
    public int LockedUntilLimitBreak { get; init; }
    /// <summary>The ship's retrofit allows <see cref="LockedBaseId"/> (checked when no limit break does).</summary>
    public bool LockedUntilRetrofit { get; init; }
    /// <summary>In Move mode, what the warehouse alone would give this slot.</summary>
    public GearSlotPlan? BagAlternative { get; init; }

    public bool Changes => Action is GearSlotAction.Bag or GearSlotAction.Move;
}

public sealed class GearShipPlan
{
    public GearFleetShip Ship { get; init; } = new("", "", 0, false, new Dictionary<int, GearItemCopy>());
    public string Usecase { get; init; } = "";
    public IReadOnlyList<GearSlotPlan> Slots { get; init; } = [];
    /// <summary>The special equipment slot; null when the ship has no community ranking and wears none.</summary>
    public GearSlotPlan? Augment { get; init; }
    /// <summary>The community usecase the special equipment ranking was read from.</summary>
    public string AugmentUsecase { get; init; } = "";
    /// <summary>Slots whose best row differs between the two armors of an alternating boss.</summary>
    public IReadOnlyList<int> SlotsDifferingInAlternateArmor { get; init; } = [];

    /// <summary>The gear slots followed by the special equipment slot, if any.</summary>
    public IEnumerable<GearSlotPlan> AllSlots => Augment is null ? Slots : Slots.Append(Augment);

    public int ChangeCount => AllSlots.Count(slot => slot.Changes);
}
