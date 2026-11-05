using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>
/// Assigns Regal-ranked gear across a whole fleet so two ships never count on the same piece.
/// Ships are served in the order given (flagship first); each slot walks its ranking from the top and
/// takes the first piece it can reach: the one already equipped, a warehouse spare, then (in Move mode)
/// one taken off a ship outside the fleet. Pieces on other ships of the fleet are never taken.
/// Regal ranks assume a fully limit-broken ship, so pieces the slot cannot take at the ship's current
/// limit break are skipped (see <see cref="GearSlotRules"/>).
/// </summary>
public static class GearAllocator
{
    /// <summary>The special equipment slot's number in plans and holders, after the five gear slots.</summary>
    public const int AugmentSlot = 6;

    public static IReadOnlyList<GearShipPlan> Allocate(
        IReadOnlyList<GearFleetShip> fleet,
        RegalGearMap map,
        string usecase,
        GearInventory inventory,
        GearSourceMode mode,
        string alternateUsecase = "")
    {
        var plans = Run(fleet, map, usecase, inventory, mode);
        if (mode == GearSourceMode.Bag) return WithArmorDifferences(plans, fleet, map, usecase, alternateUsecase, inventory);

        // What the warehouse alone would give, allocated with the same contention rules, for the "창고 대체" line.
        var bagPlans = Run(fleet, map, usecase, inventory, GearSourceMode.Bag);
        var merged = plans.Select((plan, index) => new GearShipPlan
        {
            Ship = plan.Ship,
            Usecase = plan.Usecase,
            Slots = plan.Slots.Select((slot, slotIndex) => slot.Action != GearSlotAction.Move
                ? slot
                : With(slot, bagPlans[index].Slots[slotIndex])).ToList(),
            Augment = plan.Augment is { Action: GearSlotAction.Move } augment && bagPlans[index].Augment is { } bagAugment
                ? With(augment, bagAugment)
                : plan.Augment,
            AugmentUsecase = plan.AugmentUsecase
        }).ToList();
        return WithArmorDifferences(merged, fleet, map, usecase, alternateUsecase, inventory);
    }

    private static List<GearShipPlan> Run(
        IReadOnlyList<GearFleetShip> fleet, RegalGearMap map, string usecase, GearInventory inventory, GearSourceMode mode)
    {
        var fleetUids = fleet.Select(ship => ship.Uid).ToHashSet(StringComparer.Ordinal);
        var spare = inventory.Spare.ToDictionary(pair => pair.Key,
            pair => pair.Value.OrderByDescending(enhance => enhance).ToList());
        var movable = MovablePool(inventory.Equipped, fleetUids);
        var heldByFleet = fleet
            .SelectMany(ship => ship.Current.Values.Select(item => (item.BaseId, Ship: ship)))
            .GroupBy(item => item.BaseId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Ship).Distinct().ToList());
        // Special equipment has its own id space and pools; the same contention rules apply.
        var augmentSpare = inventory.AugmentSpare.ToDictionary(pair => pair.Key,
            pair => pair.Value.OrderByDescending(enhance => enhance).ToList());
        var augmentMovable = MovablePool(inventory.AugmentEquipped, fleetUids);
        var augmentHeldByFleet = fleet.Where(ship => ship.CurrentAugment is not null)
            .GroupBy(ship => ship.CurrentAugment!.BaseId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var plans = new List<GearShipPlan>();
        foreach (var ship in fleet)
        {
            var key = map.ResolveUsecase(ship.RegalGearId, usecase, ship.IsFlagship);
            var slots = map.Slots(ship.RegalGearId, key);
            var slotPlans = new List<GearSlotPlan>();
            for (var slot = 1; slot <= 5; slot++)
            {
                var current = ship.Current.GetValueOrDefault(slot);
                var ranks = slot <= slots.Count ? slots[slot - 1] : [];
                var slotNumber = slot;
                bool SlotFits(int baseId) => Fits(ship, slotNumber, baseId, inventory);
                if (ranks.Count == 0 || !ranks.Any(row => row.Any(SlotFits)))
                {
                    // No ranking, or none of it fits this slot at the ship's current limit break.
                    var top = ranks.Count == 0 ? 0 : ranks[0][0];
                    slotPlans.Add(new GearSlotPlan
                    {
                        Slot = slot, Action = current is null ? GearSlotAction.NoData : GearSlotAction.Keep,
                        BaseId = current?.BaseId ?? 0, Enhance = current?.Enhance ?? 0, Current = current,
                        LockedBaseId = top, LockedRank = top > 0 ? 1 : 0,
                        LockedUntilLimitBreak = top > 0 ? UnlocksAt(ship, slot, top, inventory) : 0,
                        LockedUntilRetrofit = top > 0 && UnlocksWithRetrofit(ship, slot, top, inventory)
                    });
                    continue;
                }

                slotPlans.Add(Choose(slot, ranks, current, ship, spare, movable, heldByFleet, mode, inventory));
            }

            var augmentKey = ship.RegalAugmentId > 0
                ? map.ResolveUsecase(ship.RegalAugmentId, usecase, ship.IsFlagship)
                : "";
            var augmentSlots = augmentKey.Length > 0 ? map.Slots(ship.RegalAugmentId, augmentKey) : [];
            var augmentRanks = augmentSlots.Count > 0 ? augmentSlots[0] : [];
            var wornAugment = ship.CurrentAugment;
            GearSlotPlan? augment = null;
            if (augmentRanks.Count > 0)
            {
                // No limit-break rule applies to special equipment, so the slot rules are dropped for it.
                augment = Choose(AugmentSlot, augmentRanks, wornAugment, ship with { SlotRules = null }, augmentSpare,
                    augmentMovable, augmentHeldByFleet, mode, inventory);
            }
            else if (wornAugment is not null || ship.RegalAugmentId > 0)
            {
                augment = new GearSlotPlan
                {
                    Slot = AugmentSlot, Action = wornAugment is null ? GearSlotAction.NoData : GearSlotAction.Keep,
                    BaseId = wornAugment?.BaseId ?? 0, Enhance = wornAugment?.Enhance ?? 0, Current = wornAugment
                };
            }

            plans.Add(new GearShipPlan
            {
                Ship = ship, Usecase = key, Slots = slotPlans, Augment = augment, AugmentUsecase = augmentKey
            });
        }

        return plans;
    }

    // Prefer pieces on ships that are not in a sortie fleet, then the better-enhanced copy, then lower-level holders.
    private static Dictionary<int, List<GearHolder>> MovablePool(IEnumerable<GearHolder> holders, IReadOnlySet<string> fleetUids) =>
        holders.Where(holder => !fleetUids.Contains(holder.ShipUid))
            .GroupBy(holder => holder.BaseId)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(holder => holder.InSortieFleet)
                .ThenByDescending(holder => holder.Enhance)
                .ThenBy(holder => holder.ShipLevel)
                .ThenBy(holder => holder.ShipUid, StringComparer.Ordinal)
                .ToList());

    private static GearSlotPlan Choose(
        int slot,
        IReadOnlyList<IReadOnlyList<int>> ranks,
        GearItemCopy? current,
        GearFleetShip ship,
        Dictionary<int, List<int>> spare,
        Dictionary<int, List<GearHolder>> movable,
        IReadOnlyDictionary<int, List<GearFleetShip>> heldByFleet,
        GearSourceMode mode,
        GearInventory inventory)
    {
        var currentRank = current is null ? 0 : RankOf(ranks, current.BaseId);
        var blocked = (BaseId: 0, Rank: 0, By: (IReadOnlyList<string>)[]);
        var locked = (BaseId: 0, Rank: 0, LimitBreak: 0, Retrofit: false);
        for (var rank = 1; rank <= ranks.Count; rank++)
        {
            if (currentRank == rank) break;
            foreach (var baseId in ranks[rank - 1])
            {
                var reachable = spare.TryGetValue(baseId, out var copies) && copies.Count > 0;
                var holders = mode == GearSourceMode.Move ? movable.GetValueOrDefault(baseId) : null;
                if (!Fits(ship, slot, baseId, inventory))
                {
                    // Remember the best piece the player could get but this limit break cannot equip, so the
                    // panel can say why the slot settles lower. Never take it.
                    if (locked.BaseId == 0 && (reachable || holders is { Count: > 0 }))
                        locked = (baseId, rank, UnlocksAt(ship, slot, baseId, inventory),
                            UnlocksWithRetrofit(ship, slot, baseId, inventory));
                    continue;
                }

                if (reachable)
                {
                    var enhance = copies![0];
                    copies.RemoveAt(0);
                    return Plan(slot, GearSlotAction.Bag, baseId, rank, enhance, current, currentRank, null, blocked, locked);
                }

                if (holders is { Count: > 0 })
                {
                    var holder = holders[0];
                    holders.RemoveAt(0);
                    return Plan(slot, GearSlotAction.Move, baseId, rank, holder.Enhance, current, currentRank, holder, blocked,
                        locked);
                }

                if (blocked.BaseId == 0 && heldByFleet.TryGetValue(baseId, out var users))
                {
                    var others = users.Where(user => user.Uid != ship.Uid).Select(user => user.Name).ToList();
                    if (others.Count > 0) blocked = (baseId, rank, others);
                }
            }
        }

        if (current is not null)
            return Plan(slot, GearSlotAction.Keep, current.BaseId, currentRank, current.Enhance, current, currentRank, null,
                blocked, locked);

        // Empty slot and nothing reachable: name the best piece this slot can take now.
        var (missingId, missingRank) = ranks
            .SelectMany((row, index) => row.Select(baseId => (BaseId: baseId, Rank: index + 1)))
            .First(item => Fits(ship, slot, item.BaseId, inventory));
        return Plan(slot, GearSlotAction.Missing, missingId, missingRank, 0, null, 0, null, blocked, locked);
    }

    /// <summary>
    /// Whether the slot takes this piece at the ship's current limit break. Ships or pieces without type data
    /// always fit, so missing game data falls back to the plain community ranking.
    /// </summary>
    public static bool Fits(GearFleetShip ship, int slot, int baseId, GearInventory inventory) =>
        ship.SlotRules is null || ship.SlotRules.Fits(slot, inventory.Types.GetValueOrDefault(baseId));

    private static int UnlocksAt(GearFleetShip ship, int slot, int baseId, GearInventory inventory) =>
        ship.SlotRules?.UnlocksAt(slot, inventory.Types.GetValueOrDefault(baseId)) ?? 0;

    private static bool UnlocksWithRetrofit(GearFleetShip ship, int slot, int baseId, GearInventory inventory) =>
        ship.SlotRules?.UnlocksWithRetrofit(slot, inventory.Types.GetValueOrDefault(baseId)) ?? false;

    private static GearSlotPlan Plan(
        int slot, GearSlotAction action, int baseId, int rank, int enhance, GearItemCopy? current, int currentRank,
        GearHolder? moveFrom, (int BaseId, int Rank, IReadOnlyList<string> By) blocked,
        (int BaseId, int Rank, int LimitBreak, bool Retrofit) locked) => new()
    {
        Slot = slot,
        Action = action,
        BaseId = baseId,
        Rank = rank,
        Enhance = enhance,
        Current = current,
        CurrentRank = currentRank,
        MoveFrom = moveFrom,
        BlockedBaseId = blocked.BaseId,
        BlockedRank = blocked.Rank,
        BlockedBy = blocked.By,
        LockedBaseId = locked.BaseId,
        LockedRank = locked.Rank,
        LockedUntilLimitBreak = locked.LimitBreak,
        LockedUntilRetrofit = locked.Retrofit
    };

    private static GearSlotPlan With(GearSlotPlan slot, GearSlotPlan bagAlternative) => new()
    {
        Slot = slot.Slot,
        Action = slot.Action,
        BaseId = slot.BaseId,
        Rank = slot.Rank,
        Enhance = slot.Enhance,
        Current = slot.Current,
        CurrentRank = slot.CurrentRank,
        MoveFrom = slot.MoveFrom,
        BlockedBaseId = slot.BlockedBaseId,
        BlockedRank = slot.BlockedRank,
        BlockedBy = slot.BlockedBy,
        LockedBaseId = slot.LockedBaseId,
        LockedRank = slot.LockedRank,
        LockedUntilLimitBreak = slot.LockedUntilLimitBreak,
        LockedUntilRetrofit = slot.LockedUntilRetrofit,
        BagAlternative = bagAlternative
    };

    private static int RankOf(IReadOnlyList<IReadOnlyList<int>> ranks, int baseId)
    {
        for (var rank = 0; rank < ranks.Count; rank++)
            if (ranks[rank].Contains(baseId)) return rank + 1;
        return 0;
    }

    private static List<GearShipPlan> WithArmorDifferences(
        List<GearShipPlan> plans, IReadOnlyList<GearFleetShip> fleet, RegalGearMap map, string usecase,
        string alternateUsecase, GearInventory inventory)
    {
        if (alternateUsecase.Length == 0 || alternateUsecase == usecase) return plans;
        return plans.Select((plan, index) =>
        {
            var ship = fleet[index];
            var first = map.Slots(ship.RegalGearId, plan.Usecase);
            var other = map.Slots(ship.RegalGearId, map.ResolveUsecase(ship.RegalGearId, alternateUsecase, ship.IsFlagship));
            var differing = Enumerable.Range(0, Math.Min(first.Count, other.Count))
                .Where(slot => !TopRow(first[slot], ship, slot + 1, inventory)
                    .SequenceEqual(TopRow(other[slot], ship, slot + 1, inventory)))
                .Select(slot => slot + 1)
                .ToList();
            return new GearShipPlan
            {
                Ship = plan.Ship, Usecase = plan.Usecase, Slots = plan.Slots, Augment = plan.Augment,
                AugmentUsecase = plan.AugmentUsecase, SlotsDifferingInAlternateArmor = differing
            };
        }).ToList();
    }

    /// <summary>The best row this slot can actually take now, so unequippable pieces never count as a difference.</summary>
    private static IEnumerable<int> TopRow(
        IReadOnlyList<IReadOnlyList<int>> ranks, GearFleetShip ship, int slot, GearInventory inventory) =>
        ranks.Select(row => row.Where(baseId => Fits(ship, slot, baseId, inventory)).ToList())
            .FirstOrDefault(row => row.Count > 0)?.Order() ?? Enumerable.Empty<int>();
}
