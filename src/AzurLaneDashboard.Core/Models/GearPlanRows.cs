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
    IReadOnlyDictionary<int, GearItemCopy> Current);

public sealed class GearInventory
{
    /// <summary>Spare copies in the warehouse: base id → enhancement of each copy.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<int>> Spare { get; init; } = new Dictionary<int, IReadOnlyList<int>>();
    /// <summary>Every equipped piece in the dock; pieces on the planned fleet are filtered out by the allocator.</summary>
    public IReadOnlyList<GearHolder> Equipped { get; init; } = [];
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
    /// <summary>In Move mode, what the warehouse alone would give this slot.</summary>
    public GearSlotPlan? BagAlternative { get; init; }

    public bool Changes => Action is GearSlotAction.Bag or GearSlotAction.Move;
}

public sealed class GearShipPlan
{
    public GearFleetShip Ship { get; init; } = new("", "", 0, false, new Dictionary<int, GearItemCopy>());
    public string Usecase { get; init; } = "";
    public IReadOnlyList<GearSlotPlan> Slots { get; init; } = [];
    /// <summary>Slots whose best row differs between the two armors of an alternating boss.</summary>
    public IReadOnlyList<int> SlotsDifferingInAlternateArmor { get; init; } = [];

    public int ChangeCount => Slots.Count(slot => slot.Changes);
}
