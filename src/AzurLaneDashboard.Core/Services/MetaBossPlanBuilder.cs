using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

/// <summary>
/// Recommends a fleet for one META boss. A verified guide's slot choices come first, in the guide's
/// order; anything it cannot fill is picked by Regal's META rating and the boss armor's damage figure.
/// </summary>
public static class MetaBossPlanBuilder
{
    public const string RatingKey = "META";

    public static MetaBossPlan Build(
        MetaBoss boss,
        MetaBossGuide? guide,
        IReadOnlyList<FleetPlannerShip> ships,
        IReadOnlyList<RegalStarRule> starRules,
        DateTime serverTime,
        IReadOnlyList<IReadOnlySet<string>>? gameFleets = null)
    {
        var usableGuide = guide is { Verified: true } ? guide : null;
        var armors = MetaBossCatalog.Armors(boss, usableGuide);
        var metrics = MetaBossCatalog.DamageMetrics(armors);
        var exempt = starRules.Where(rule => rule.Applies(boss, armors)).Select(rule => rule.RegalUid)
            .ToHashSet(StringComparer.Ordinal);
        var owned = ships.Where(ship => ship.Position is "Main" or "Vanguard").ToList();
        var used = new HashSet<string>(StringComparer.Ordinal);

        var main = FillPosition(usableGuide?.Main ?? [], "Main", owned, used, exempt, metrics);
        var vanguard = FillPosition(usableGuide?.Vanguard ?? [], "Vanguard", owned, used, exempt, metrics);
        // A guide's flagship stands; otherwise Regal's flag requirement picks it, and no flagship is claimed when
        // no ship needs the spot.
        var flagshipIndex = Math.Clamp(usableGuide?.FlagshipIndex ?? 0, 0, main.Count - 1);
        if (usableGuide is not null && main[flagshipIndex] is { IsAuto: false } flagship)
            main[flagshipIndex] = Copy(flagship, isFlagship: true);
        else
            main = OrderFlagship(main);
        vanguard = OrderLead(vanguard);

        var fleetUids = main.Concat(vanguard).OfType<MetaBossSlot>().Select(slot => slot.ShipUid)
            .ToHashSet(StringComparer.Ordinal);
        return new MetaBossPlan
        {
            Boss = boss,
            Guide = usableGuide,
            UsesGuide = usableGuide is not null,
            Armors = armors,
            DamageMetrics = metrics,
            GearUsecase = MetaBossCatalog.GearUsecase(armors),
            Mechanics = usableGuide?.Mechanics.Count > 0
                ? usableGuide.Mechanics
                : boss.Skills.Select(skill => new MetaBossMechanic(skill.Name, skill.Text)).ToList(),
            MainSlots = main,
            VanguardSlots = vanguard,
            IsCurrent = boss.IsActive(serverTime),
            EndsAt = boss.ActiveWindow(serverTime)?.End,
            GameFleetOverlap = (gameFleets ?? []).Select(fleet => fleet.Count(fleetUids.Contains)).DefaultIfEmpty(0).Max()
        };
    }

    private static List<MetaBossSlot?> FillPosition(
        IReadOnlyList<IReadOnlyList<string>> guideSlots,
        string position,
        IReadOnlyList<FleetPlannerShip> owned,
        HashSet<string> used,
        IReadOnlySet<string> exempt,
        IReadOnlyList<string> metrics)
    {
        var slots = new List<MetaBossSlot?>();
        for (var index = 0; index < FleetPlanBuilder.SlotsPerPosition; index++)
        {
            var choices = index < guideSlots.Count ? guideSlots[index] : [];
            slots.Add(FromGuide(choices, position, owned, used, exempt));
        }

        // Empty slots are filled by rating, then by the damage the boss's armor lets the ship deal.
        var ranked = owned
            .Where(ship => ship.Position == position && ship.Level >= FleetPlanBuilder.ReadyLevel &&
                           !used.Contains(ship.Uid) && Score(ship, exempt) > 0)
            .GroupBy(ship => ship.RegalUid.Length > 0 ? ship.RegalUid : ship.Uid)
            .Select(group => group.OrderByDescending(ship => ship.Level).First())
            .OrderByDescending(ship => Score(ship, exempt))
            .ThenByDescending(ship => Damage(ship, metrics))
            .ThenBy(ship => ship.Uid, StringComparer.Ordinal)
            .ToList();
        var next = 0;
        for (var index = 0; index < slots.Count; index++)
        {
            if (slots[index] is not null) continue;
            while (next < ranked.Count && used.Contains(ranked[next].Uid)) next++;
            if (next >= ranked.Count) break;
            var ship = ranked[next++];
            used.Add(ship.Uid);
            slots[index] = new MetaBossSlot
            {
                Ship = ship, Source = MetaBossSlotSource.Auto, Rating = Rating(ship), Note = ""
            };
        }

        return slots;
    }

    private static MetaBossSlot? FromGuide(
        IReadOnlyList<string> choices, string position, IReadOnlyList<FleetPlannerShip> owned, HashSet<string> used,
        IReadOnlySet<string> exempt)
    {
        string? passedOver = null;
        for (var rank = 0; rank < choices.Count; rank++)
        {
            var copies = owned.Where(ship => ship.RegalUid == choices[rank] && ship.Position == position &&
                                             !used.Contains(ship.Uid))
                .OrderByDescending(ship => ship.Level)
                .ToList();
            var ready = copies.FirstOrDefault(ship => ship.Level >= FleetPlanBuilder.ReadyLevel);
            if (ready is null)
            {
                // Remember the guide's preferred ship the player owns but has not levelled, to explain the swap.
                if (copies.Count > 0) passedOver ??= $"{copies[0].Name} Lv.{copies[0].Level} 대신";
                continue;
            }

            used.Add(ready.Uid);
            return new MetaBossSlot
            {
                Ship = ready,
                Source = rank == 0 ? MetaBossSlotSource.Guide : MetaBossSlotSource.GuideAlternative,
                Rating = Rating(ready),
                Note = rank == 0 ? "" : passedOver ?? "1순위 미보유"
            };
        }

        return null;
    }

    private static MetaBossSlot Copy(MetaBossSlot slot, bool isFlagship) => new()
    {
        Ship = slot.Ship, Source = slot.Source, Rating = slot.Rating, Note = slot.Note, IsFlagship = isFlagship,
        FlagshipText = slot.FlagshipText, IsLead = slot.IsLead, LeadText = slot.LeadText
    };

    private static List<MetaBossSlot?> OrderFlagship(List<MetaBossSlot?> main)
    {
        var filled = FleetSlotOrder.FlagshipFirst(main.OfType<MetaBossSlot>().ToList(), slot => slot.Ship);
        if (filled.Count > 0 && FleetSlotOrder.FlagNeed(filled[0].Ship) > 0)
        {
            var flagship = filled[0];
            filled[0] = new MetaBossSlot
            {
                Ship = flagship.Ship, Source = flagship.Source, Rating = flagship.Rating, Note = flagship.Note,
                IsFlagship = true, FlagshipText = FleetSlotOrder.FlagshipText(flagship.Ship)
            };
        }

        return filled.Cast<MetaBossSlot?>().Concat(Enumerable.Repeat<MetaBossSlot?>(null, main.Count - filled.Count)).ToList();
    }

    // The guide's lead keeps the front. When the guide leaves it open (no guide, or its lead choices are not owned
    // at Lv.120), the most survivable vanguard by Regal leads instead of whichever ship rated highest.
    private static List<MetaBossSlot?> OrderLead(List<MetaBossSlot?> vanguard)
    {
        if (vanguard.FirstOrDefault() is { IsAuto: false } guideLead)
        {
            vanguard[0] = new MetaBossSlot
            {
                Ship = guideLead.Ship, Source = guideLead.Source, Rating = guideLead.Rating, Note = guideLead.Note,
                IsLead = true, LeadText = guideLead.Source == MetaBossSlotSource.Guide ? "선두 · 공략 1순위" : "선두 · 공략"
            };
            return vanguard;
        }

        var filled = FleetSlotOrder.LeadFirst(vanguard.OfType<MetaBossSlot>().ToList(), slot => slot.Ship);
        if (filled.Count == 0) return vanguard;
        var lead = filled[0];
        filled[0] = new MetaBossSlot
        {
            Ship = lead.Ship, Source = lead.Source, Rating = lead.Rating, Note = lead.Note,
            IsLead = true, LeadText = FleetSlotOrder.LeadText(lead.Ship)
        };
        return filled.Cast<MetaBossSlot?>().Concat(Enumerable.Repeat<MetaBossSlot?>(null, vanguard.Count - filled.Count)).ToList();
    }

    private static string Rating(FleetPlannerShip ship) => ship.Ratings.GetValueOrDefault(RatingKey) ?? "";

    // An asterisk rating only holds for some bosses: it keeps its score when a rule says this boss qualifies.
    private static int Score(FleetPlannerShip ship, IReadOnlySet<string> exempt)
    {
        var rating = Rating(ship);
        var score = FleetPlanRating.Score(rating);
        return rating.Contains('*', StringComparison.Ordinal) && !exempt.Contains(ship.RegalUid) ? score - 2 : score;
    }

    private static double Damage(FleetPlannerShip ship, IReadOnlyList<string> metrics) =>
        metrics.Count == 0 ? 0 : metrics.Average(metric => ship.Metrics.GetValueOrDefault(metric));
}
