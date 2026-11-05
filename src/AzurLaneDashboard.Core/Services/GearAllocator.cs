using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>
/// Assigns Regal-ranked gear across a whole fleet so two ships never count on the same piece.
/// Ships are served in the order given (flagship first); each slot walks its ranking from the top and
/// takes the first piece it can reach: the one already equipped, a warehouse spare, then (in Move mode)
/// one taken off a ship outside the fleet. Pieces on other ships of the fleet are never taken.
/// </summary>
public static class GearAllocator
{
    public static IReadOnlyList<GearShipPlan> Allocate(
        IReadOnlyList<GearFleetShip> fleet,
        RegalGearMap map,
        string usecase,
        GearInventory inventory,
        GearSourceMode mode,
        string alternateUsecase = "")
    {
        var plans = Run(fleet, map, usecase, inventory, mode);
        if (mode == GearSourceMode.Bag) return WithArmorDifferences(plans, fleet, map, usecase, alternateUsecase);

        // What the warehouse alone would give, allocated with the same contention rules, for the "창고 대체" line.
        var bagPlans = Run(fleet, map, usecase, inventory, GearSourceMode.Bag);
        var merged = plans.Select((plan, index) => new GearShipPlan
        {
            Ship = plan.Ship,
            Usecase = plan.Usecase,
            Slots = plan.Slots.Select((slot, slotIndex) => slot.Action != GearSlotAction.Move
                ? slot
                : With(slot, bagPlans[index].Slots[slotIndex])).ToList()
        }).ToList();
        return WithArmorDifferences(merged, fleet, map, usecase, alternateUsecase);
    }

    private static List<GearShipPlan> Run(
        IReadOnlyList<GearFleetShip> fleet, RegalGearMap map, string usecase, GearInventory inventory, GearSourceMode mode)
    {
        var fleetUids = fleet.Select(ship => ship.Uid).ToHashSet(StringComparer.Ordinal);
        var spare = inventory.Spare.ToDictionary(pair => pair.Key,
            pair => pair.Value.OrderByDescending(enhance => enhance).ToList());
        // Prefer pieces on ships that are not in a sortie fleet, then the better-enhanced copy, then lower-level holders.
        var movable = inventory.Equipped.Where(holder => !fleetUids.Contains(holder.ShipUid))
            .GroupBy(holder => holder.BaseId)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(holder => holder.InSortieFleet)
                .ThenByDescending(holder => holder.Enhance)
                .ThenBy(holder => holder.ShipLevel)
                .ThenBy(holder => holder.ShipUid, StringComparer.Ordinal)
                .ToList());
        var heldByFleet = fleet
            .SelectMany(ship => ship.Current.Values.Select(item => (item.BaseId, Ship: ship)))
            .GroupBy(item => item.BaseId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Ship).Distinct().ToList());

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
                slotPlans.Add(ranks.Count == 0
                    ? new GearSlotPlan
                    {
                        Slot = slot, Action = current is null ? GearSlotAction.NoData : GearSlotAction.Keep,
                        BaseId = current?.BaseId ?? 0, Enhance = current?.Enhance ?? 0, Current = current
                    }
                    : Choose(slot, ranks, current, ship, spare, movable, heldByFleet, mode));
            }

            plans.Add(new GearShipPlan { Ship = ship, Usecase = key, Slots = slotPlans });
        }

        return plans;
    }

    private static GearSlotPlan Choose(
        int slot,
        IReadOnlyList<IReadOnlyList<int>> ranks,
        GearItemCopy? current,
        GearFleetShip ship,
        Dictionary<int, List<int>> spare,
        Dictionary<int, List<GearHolder>> movable,
        IReadOnlyDictionary<int, List<GearFleetShip>> heldByFleet,
        GearSourceMode mode)
    {
        var currentRank = current is null ? 0 : RankOf(ranks, current.BaseId);
        var blocked = (BaseId: 0, Rank: 0, By: (IReadOnlyList<string>)[]);
        for (var rank = 1; rank <= ranks.Count; rank++)
        {
            if (currentRank == rank) break;
            foreach (var baseId in ranks[rank - 1])
            {
                if (spare.TryGetValue(baseId, out var copies) && copies.Count > 0)
                {
                    var enhance = copies[0];
                    copies.RemoveAt(0);
                    return Plan(slot, GearSlotAction.Bag, baseId, rank, enhance, current, currentRank, null, blocked);
                }

                if (mode == GearSourceMode.Move && movable.TryGetValue(baseId, out var holders) && holders.Count > 0)
                {
                    var holder = holders[0];
                    holders.RemoveAt(0);
                    return Plan(slot, GearSlotAction.Move, baseId, rank, holder.Enhance, current, currentRank, holder, blocked);
                }

                if (blocked.BaseId == 0 && heldByFleet.TryGetValue(baseId, out var users))
                {
                    var others = users.Where(user => user.Uid != ship.Uid).Select(user => user.Name).ToList();
                    if (others.Count > 0) blocked = (baseId, rank, others);
                }
            }
        }

        return current is null
            ? Plan(slot, GearSlotAction.Missing, ranks[0][0], 1, 0, null, 0, null, blocked)
            : Plan(slot, GearSlotAction.Keep, current.BaseId, currentRank, current.Enhance, current, currentRank, null, blocked);
    }

    private static GearSlotPlan Plan(
        int slot, GearSlotAction action, int baseId, int rank, int enhance, GearItemCopy? current, int currentRank,
        GearHolder? moveFrom, (int BaseId, int Rank, IReadOnlyList<string> By) blocked) => new()
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
        BlockedBy = blocked.By
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
        string alternateUsecase)
    {
        if (alternateUsecase.Length == 0 || alternateUsecase == usecase) return plans;
        return plans.Select((plan, index) =>
        {
            var ship = fleet[index];
            var first = map.Slots(ship.RegalGearId, plan.Usecase);
            var other = map.Slots(ship.RegalGearId, map.ResolveUsecase(ship.RegalGearId, alternateUsecase, ship.IsFlagship));
            var differing = Enumerable.Range(0, Math.Min(first.Count, other.Count))
                .Where(slot => !TopRow(first[slot]).SequenceEqual(TopRow(other[slot])))
                .Select(slot => slot + 1)
                .ToList();
            return new GearShipPlan { Ship = plan.Ship, Usecase = plan.Usecase, Slots = plan.Slots, SlotsDifferingInAlternateArmor = differing };
        }).ToList();
    }

    private static IEnumerable<int> TopRow(IReadOnlyList<IReadOnlyList<int>> ranks) =>
        ranks.Count == 0 ? [] : ranks[0].Order();
}
