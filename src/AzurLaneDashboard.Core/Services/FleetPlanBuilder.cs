using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Core.Services;

public sealed record FleetPlannerShip
{
    public string Uid { get; init; } = "";
    public int GroupKey { get; init; }
    /// <summary>The matched community entry ("405074_Götz von Berlichingen"); retrofits share a group but not a uid.</summary>
    public string RegalUid { get; init; } = "";
    /// <summary>Key into regal_gear_map.json's ship table; 0 when unknown.</summary>
    public int RegalGearId { get; init; }
    /// <summary>The same table's key for the special equipment (augment) ranking; 0 when unknown.</summary>
    public int RegalAugmentId { get; init; }
    public string Name { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string Hull { get; init; } = "";
    /// <summary>Community fleet position: "Main", "Vanguard" or "Submarine".</summary>
    public string Position { get; init; } = "";
    public int Level { get; init; }
    public IReadOnlyDictionary<string, string> Ratings { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, double> Metrics { get; init; } = new Dictionary<string, double>();
    public IReadOnlyList<FleetPlannerPairing> Pairings { get; init; } = [];
    public int? ExperienceToReady { get; init; }
    public int SampleExp { get; init; }
    public int SkillGap { get; init; }
    public int EmptySlots { get; init; }
}

/// <summary>Ratings that apply only while one of the partner ship groups shares the fleet.</summary>
public sealed record FleetPlannerPairing(IReadOnlyList<int> PartnerGroups, IReadOnlyDictionary<string, string> Ratings);

public sealed record FleetPlanContext(string RatingKey, string FleetTitle, string Label, bool IsBoss);

public sealed record FleetPlanContentDefinition(string Key, string Label, string Group, IReadOnlyList<FleetPlanContext> Contexts);

public static class FleetPlanBuilder
{
    public const int ReadyLevel = 120;
    private const int MaximumCandidates = 4;
    public const int SlotsPerPosition = 3;

    public static IReadOnlyList<FleetPlanContentDefinition> Contents { get; } =
    [
        new("w1516", "15·16 해역", "해역",
        [
            new("W15/16 Boss", "보스 함대", "15·16 해역 보스", true),
            new("W15/16 Mob", "잡몹 함대", "15·16 해역 잡몹", false)
        ]),
        new("w14", "14 해역", "해역",
        [
            new("W14 Boss", "보스 함대", "14 해역 보스", true),
            new("W14 Mob", "잡몹 함대", "14 해역 잡몹", false)
        ]),
        new("arbiter", "하드 아비터", "단일 보스전", [new("Hard Arbiter", "출격 함대", "하드 아비터", true)]),
        new("meta", "META 보스전", "단일 보스전", [new("META", "출격 함대", "META 보스전", true)]),
        new("cm", "챌린지 모드", "단일 보스전", [new("CM", "출격 함대", "챌린지 모드", true)]),
        new("ex", "EX 랭킹", "단일 보스전", [new("EX", "출격 함대", "EX 랭킹", true)])
    ];

    private static readonly IReadOnlyDictionary<string, string> HullLabels = new Dictionary<string, string>
    {
        ["DD"] = "구축", ["CL"] = "경순", ["CA"] = "중순", ["CB"] = "대형순양", ["BC"] = "순양전함",
        ["BB"] = "전함", ["BBV"] = "항공전함", ["CV"] = "항모", ["CVL"] = "경항모", ["BM"] = "모니터",
        ["SS"] = "잠수", ["SSV"] = "잠수항모", ["AR"] = "공작", ["AE"] = "운송",
        ["DDGv"] = "미사일구축", ["DDGm"] = "미사일구축", ["IXs"] = "범선", ["IXv"] = "범선", ["IXm"] = "범선"
    };

    public static FleetPlanDashboard Build(
        IReadOnlyList<FleetPlannerShip> ships,
        IReadOnlyList<IReadOnlySet<string>> gameFleets,
        IReadOnlyList<CommunityRatingChangeRow>? ratingChanges = null,
        string ratingSourceText = "")
    {
        var owned = ships
            .Where(ship => ship.GroupKey > 0 && ship.Position is "Main" or "Vanguard")
            .GroupBy(ship => ship.GroupKey)
            .Select(group => group.OrderByDescending(ship => ship.Level).First())
            .ToList();
        var ready = owned.Where(ship => ship.Level >= ReadyLevel).ToList();

        var drafts = Contents.Select(content => BuildContent(content, owned, ready, gameFleets)).ToList();
        var reach = drafts
            .SelectMany(draft => draft.Candidates.Select(candidate => candidate.Ship.Uid).Distinct())
            .GroupBy(uid => uid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return new FleetPlanDashboard
        {
            Contents = drafts.Select(draft => draft.ToContent(reach)).ToList(),
            RatingChanges = ratingChanges ?? [],
            RatingSourceText = ratingSourceText,
            OwnedShipCount = owned.Count,
            ReadyShipCount = ready.Count,
            ReadyLevel = ReadyLevel
        };
    }

    public static string HullLabel(string hull) => HullLabels.GetValueOrDefault(hull, hull);

    private static ContentDraft BuildContent(
        FleetPlanContentDefinition content,
        IReadOnlyList<FleetPlannerShip> owned,
        IReadOnlyList<FleetPlannerShip> ready,
        IReadOnlyList<IReadOnlySet<string>> gameFleets)
    {
        var readyUsed = new HashSet<int>();
        var potentialUsed = new HashSet<int>();
        var fleets = new List<FleetDraft>();
        var candidates = new List<CandidateDraft>();
        foreach (var context in content.Contexts)
        {
            // The picks are the best six; then the flagship and the lead are placed by Regal's position fields.
            var readyFleet = new FleetDraft(context,
                FleetSlotOrder.FlagshipFirst(Pick(ready, context, "Main", readyUsed), ship => ship),
                FleetSlotOrder.LeadFirst(Pick(ready, context, "Vanguard", readyUsed), ship => ship));
            fleets.Add(readyFleet);
            foreach (var position in new[] { "Main", "Vanguard" })
            {
                var potential = Pick(owned, context, position, potentialUsed);
                candidates.AddRange(FindCandidates(context, position, readyFleet.Position(position), potential));
            }
        }

        return new ContentDraft(content, fleets, candidates, gameFleets);
    }

    private static List<FleetPlannerShip> Pick(
        IEnumerable<FleetPlannerShip> pool, FleetPlanContext context, string position, HashSet<int> used)
    {
        var picked = pool
            .Where(ship => ship.Position == position && !used.Contains(ship.GroupKey) && Score(ship, context) > 0)
            .OrderByDescending(ship => RankScore(ship, context))
            .ThenByDescending(ship => Damage(ship, context))
            .ThenByDescending(ship => ship.Level)
            .ThenBy(ship => ship.Uid, StringComparer.Ordinal)
            .Take(SlotsPerPosition)
            .ToList();
        foreach (var ship in picked)
            used.Add(ship.GroupKey);
        return picked;
    }

    private static IEnumerable<CandidateDraft> FindCandidates(
        FleetPlanContext context, string position, IReadOnlyList<FleetPlannerShip> readySlots,
        IReadOnlyList<FleetPlannerShip> potential)
    {
        var readyUids = readySlots.Select(ship => ship.Uid).ToHashSet(StringComparer.Ordinal);
        // Each newcomer displaces the weakest ready ship it has not already displaced.
        var replaceable = new Queue<FleetPlannerShip>(readySlots
            .OrderBy(ship => RankScore(ship, context))
            .ThenBy(ship => Damage(ship, context)));
        var emptySlots = SlotsPerPosition - readySlots.Count;
        foreach (var ship in potential.Where(ship => !readyUids.Contains(ship.Uid) && ship.Level < ReadyLevel))
        {
            FleetPlannerShip? replaced = null;
            if (emptySlots > 0)
                emptySlots--;
            else if (!replaceable.TryDequeue(out replaced))
                yield break;

            var gain = Gain(ship, replaced, context, out var damageDelta);
            if (gain <= 0)
                continue;
            yield return new CandidateDraft(ship, replaced, context, position, gain, damageDelta);
        }
    }

    private static double Gain(FleetPlannerShip ship, FleetPlannerShip? replaced, FleetPlanContext context,
        out int? damageDelta)
    {
        damageDelta = null;
        if (replaced is null)
            return Score(ship, context);

        var newDamage = Damage(ship, context);
        var oldDamage = Damage(replaced, context);
        if (newDamage > 0 && oldDamage > 0)
            damageDelta = (int)Math.Round((newDamage - oldDamage) * 100 / oldDamage, MidpointRounding.AwayFromZero);
        var ratingGain = Math.Max(0, Score(ship, context) - Score(replaced, context)) * 3;
        return Math.Max(0, damageDelta ?? 0) + ratingGain;
    }

    private static int Score(FleetPlannerShip ship, FleetPlanContext context) =>
        FleetPlanRating.Score(ship.Ratings.GetValueOrDefault(context.RatingKey));

    // Asterisk ratings only hold against specific bosses, so they lose ties to unconditional ones.
    private static int RankScore(FleetPlannerShip ship, FleetPlanContext context)
    {
        var rating = ship.Ratings.GetValueOrDefault(context.RatingKey) ?? "";
        return Score(ship, context) - (rating.Contains('*', StringComparison.Ordinal) ? 2 : 0);
    }

    private static double Damage(FleetPlannerShip ship, FleetPlanContext context) =>
        ship.Metrics.GetValueOrDefault(context.IsBoss ? "Heavy Dmg" : "Light Dmg");

    private static bool HasMetric(FleetPlannerShip ship, string key) => ship.Metrics.GetValueOrDefault(key) > 0;

    private sealed record FleetDraft(FleetPlanContext Context, List<FleetPlannerShip> Main, List<FleetPlannerShip> Vanguard)
    {
        public IReadOnlyList<FleetPlannerShip> Position(string position) => position == "Main" ? Main : Vanguard;
        public IEnumerable<FleetPlannerShip> Ships => Main.Concat(Vanguard);
    }

    private sealed record CandidateDraft(
        FleetPlannerShip Ship, FleetPlannerShip? Replaced, FleetPlanContext Context, string Position,
        double Gain, int? DamageDelta);

    private sealed class ContentDraft(
        FleetPlanContentDefinition definition,
        List<FleetDraft> fleets,
        List<CandidateDraft> candidates,
        IReadOnlyList<IReadOnlySet<string>> gameFleets)
    {
        public IReadOnlyList<CandidateDraft> Candidates => candidates;

        public FleetPlanContent ToContent(IReadOnlyDictionary<string, int> reach)
        {
            var ranked = candidates
                .GroupBy(candidate => candidate.Ship.Uid, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(candidate => candidate.Gain).First())
                .Select(candidate => (Candidate: candidate, Reach: reach.GetValueOrDefault(candidate.Ship.Uid, 1)))
                .OrderByDescending(item => Value(item.Candidate, item.Reach))
                .ThenBy(item => item.Candidate.Ship.Uid, StringComparer.Ordinal)
                .Take(MaximumCandidates)
                .ToList();
            var upgradeNames = ranked
                .Where(item => item.Candidate.Replaced is not null)
                .GroupBy(item => item.Candidate.Replaced!.Uid, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Candidate.Ship.Name, StringComparer.Ordinal);

            return new FleetPlanContent
            {
                Key = definition.Key,
                Label = definition.Label,
                Group = definition.Group,
                // A context nobody qualifies for would only show an empty card; the empty state explains it instead.
                Fleets = fleets.Where(fleet => fleet.Ships.Any()).Select(fleet => ToFleet(fleet, upgradeNames)).ToList(),
                Candidates = ranked.Select((item, index) => ToCandidate(item.Candidate, item.Reach, index + 1)).ToList(),
                FinishItems = fleets.SelectMany(fleet => fleet.Ships).Select(ToFinishItem).OfType<FleetPlanFinishItem>().ToList(),
                // Growth goals track level and skills only, so missing gear is reported apart from them.
                EquipmentGaps = fleets.SelectMany(fleet => fleet.Ships)
                    .Where(ship => ship.EmptySlots > 0)
                    .Select(ship => new FleetPlanFinishItem
                    {
                        ShipUid = ship.Uid, Name = ship.Name, Level = ship.Level, Detail = $"빈칸 {ship.EmptySlots}"
                    })
                    .ToList()
            };
        }

        // Cheap improvements and ships reused across contents rise; EXP cost is in millions.
        private static double Value(CandidateDraft candidate, int reach)
        {
            var cost = Math.Max(0.25, (candidate.Ship.ExperienceToReady ?? 3_000_000) / 1_000_000.0);
            return (candidate.Gain + (reach - 1) * 15) / cost;
        }

        private FleetPlanFleet ToFleet(FleetDraft fleet, IReadOnlyDictionary<string, string> upgradeNames)
        {
            var groups = fleet.Ships.Select(ship => ship.GroupKey).ToHashSet();
            var uids = fleet.Ships.Select(ship => ship.Uid).ToHashSet(StringComparer.Ordinal);
            var overlap = gameFleets.Count == 0 ? 0 : gameFleets.Max(game => game.Count(uids.Contains));
            return new FleetPlanFleet
            {
                Title = fleet.Context.FleetTitle,
                ContextLabel = fleet.Context.Label,
                MainSlots = fleet.Main.Select((ship, index) =>
                    ToSlot(ship, fleet.Context, groups, fleet.Ships, upgradeNames,
                        index == 0 ? FleetSlotOrder.FlagshipText(ship) : "")).ToList(),
                VanguardSlots = fleet.Vanguard.Select((ship, index) =>
                    ToSlot(ship, fleet.Context, groups, fleet.Ships, upgradeNames,
                        index == 0 ? FleetSlotOrder.LeadText(ship) : "")).ToList(),
                GameFleetOverlap = overlap
            };
        }

        private static FleetPlanSlot ToSlot(FleetPlannerShip ship, FleetPlanContext context, IReadOnlySet<int> fleetGroups,
            IEnumerable<FleetPlannerShip> fleetShips, IReadOnlyDictionary<string, string> upgradeNames, string positionText = "")
        {
            var rating = ship.Ratings.GetValueOrDefault(context.RatingKey) ?? "";
            var partnerName = "";
            foreach (var pairing in ship.Pairings)
            {
                var partnerGroup = pairing.PartnerGroups.FirstOrDefault(fleetGroups.Contains);
                var paired = pairing.Ratings.GetValueOrDefault(context.RatingKey) ?? "";
                if (partnerGroup <= 0 || FleetPlanRating.Score(paired) < FleetPlanRating.Score(rating))
                    continue;
                rating = paired;
                partnerName = fleetShips.First(other => other.GroupKey == partnerGroup).Name;
            }

            var roles = new List<string>();
            if (HasMetric(ship, "Offense Buff")) roles.Add("공격 버프");
            if (HasMetric(ship, "Main Defense")) roles.Add("주력 방어");
            if (HasMetric(ship, "VG Defense")) roles.Add("전위 방어");
            if (HasMetric(ship, "AA")) roles.Add("대공");
            return new FleetPlanSlot
            {
                ShipUid = ship.Uid,
                Name = ship.Name,
                IconUrl = ship.IconUrl,
                Rarity = ship.Rarity,
                HullLabel = HullLabel(ship.Hull),
                Level = ship.Level,
                Rating = rating,
                Roles = roles,
                PartnerName = partnerName,
                UpgradeCandidateName = upgradeNames.GetValueOrDefault(ship.Uid, ""),
                HasOffenseBuff = HasMetric(ship, "Offense Buff"),
                HasDefense = HasMetric(ship, "Main Defense") || HasMetric(ship, "VG Defense"),
                HasAntiAir = HasMetric(ship, "AA"),
                PositionText = positionText
            };
        }

        private static FleetPlanCandidate ToCandidate(CandidateDraft candidate, int reach, int rank)
        {
            var ship = candidate.Ship;
            int? battles = ship.ExperienceToReady is { } experience && ship.SampleExp > 0
                ? (experience + ship.SampleExp - 1) / ship.SampleExp
                : null;
            return new FleetPlanCandidate
            {
                Rank = rank,
                ShipUid = ship.Uid,
                Name = ship.Name,
                IconUrl = ship.IconUrl,
                Rarity = ship.Rarity,
                HullLabel = HullLabel(ship.Hull),
                Level = ship.Level,
                TargetLevel = ReadyLevel,
                FleetTitle = candidate.Context.FleetTitle,
                PositionLabel = candidate.Position == "Main" ? "주력" : "전위",
                ReplacesUid = candidate.Replaced?.Uid ?? "",
                ReplacesName = candidate.Replaced?.Name ?? "",
                ReplacesRating = candidate.Replaced?.Ratings.GetValueOrDefault(candidate.Context.RatingKey) ?? "",
                Rating = ship.Ratings.GetValueOrDefault(candidate.Context.RatingKey) ?? "",
                DamageDeltaPercent = candidate.DamageDelta,
                ContentReach = reach,
                ExperienceNeeded = ship.ExperienceToReady,
                EstimatedBattles = battles,
                SampleExp = ship.SampleExp
            };
        }

        private static FleetPlanFinishItem? ToFinishItem(FleetPlannerShip ship)
        {
            var parts = new List<string>();
            if (ship.Level < ShipExperienceCalculator.MaximumLevel) parts.Add($"Lv.{ship.Level} → 125");
            if (ship.SkillGap > 0) parts.Add($"스킬 {ship.SkillGap}단계");
            return parts.Count == 0
                ? null
                : new FleetPlanFinishItem { ShipUid = ship.Uid, Name = ship.Name, Level = ship.Level, Detail = string.Join(" · ", parts) };
        }
    }
}
