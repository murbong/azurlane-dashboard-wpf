using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static FleetPlanDashboard BuildFleetPlan(
        SqliteConnection connection,
        List<ShipState> ships,
        SnapshotMaps maps,
        IReadOnlyList<FleetCompositionGroupRow> fleetCompositionGroups,
        IReadOnlyList<EquipmentRow> equipment,
        string projectRoot)
    {
        // Regal's ratings are downloaded by the data update and are not shipped, so before the first update no ship
        // is rated. The inputs that do not need ratings (META fights, bosses, guides) still load.
        var hasRegal = maps.RegalShips.ValueKind == JsonValueKind.Array;
        var regalIndex = BuildRegalShipIndex(maps.RegalShips);
        var plannerShips = new List<FleetPlannerShip>();
        var levelingShips = new List<LevelingShip>();
        var englishToKorean = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ship in ships.Where(ship => hasRegal && !ship.IsBulin && ship.HasFullState))
        {
            var regal = MatchRegalShip(ship, regalIndex);
            var regalGroup = FirstPositive(JsonInt(regal, "group"), ship.TemplateId / 10);
            if (regal.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = ship.Name.Trim();
            var englishName = BaseRegalName(JsonString(regal, "name"));
            if (englishName.Length > 0)
            {
                englishToKorean.TryAdd(englishName, name);
            }

            var ratings = Child(regal, "ratings");
            var planner = new FleetPlannerShip
            {
                Uid = ship.Uid,
                GroupKey = regalGroup,
                RegalUid = JsonString(regal, "uid"),
                RegalGearId = JsonInt(regal, "gear"),
                Name = name,
                IconUrl = ship.IconUrl,
                Rarity = ship.Rarity,
                Hull = JsonString(regal, "hull"),
                Position = JsonString(regal, "type"),
                Level = ship.Level,
                Ratings = RegalTextRatings(ratings),
                Metrics = RegalNumericRatings(ratings),
                Pairings = RegalPairings(regalIndex.ByGroup.GetValueOrDefault(regalGroup) ?? []),
                ExperienceToReady = ExpRemainingToLevel(ship.Level, ship.Exp, FleetPlanBuilder.ReadyLevel, ship.Rarity,
                    maps.ShipLevels),
                SampleExp = ship.RecentBattleExpAvg,
                SkillGap = ship.SkillGap,
                EmptySlots = ship.EmptySlots
            };
            plannerShips.Add(planner);
            levelingShips.Add(ToLevelingShip(ship, planner, maps));
        }

        var gameFleets = fleetCompositionGroups
            .Select(group => (IReadOnlySet<string>)group.Ships
                .Select(row => row.ShipUid)
                .Where(uid => !string.IsNullOrWhiteSpace(uid))
                .ToHashSet(StringComparer.Ordinal))
            .Where(set => set.Count > 0)
            .ToList();
        var latest = CommunityRatingChangeParser.LatestDate(maps.RegalChangelog);
        var plan = FleetPlanBuilder.Build(
            plannerShips,
            gameFleets,
            CommunityRatingChangeParser.Parse(maps.RegalChangelog, englishToKorean),
            latest.Length > 0 ? $"Regal · {latest}" : "Regal");
        return plan.WithInputs(BuildFleetPlannerInputs(connection, ships, maps, plannerShips, levelingShips, plan,
            fleetCompositionGroups, equipment, projectRoot));
    }

    private static string BaseRegalName(string name)
    {
        var index = name.IndexOf(" (", StringComparison.Ordinal);
        return (index > 0 ? name[..index] : name).Trim();
    }

    private static IReadOnlyDictionary<string, string> RegalTextRatings(JsonElement ratings)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (ratings.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in ratings.EnumerateObject().Where(property => property.Value.ValueKind == JsonValueKind.String))
        {
            result[property.Name] = property.Value.GetString() ?? "";
        }

        return result;
    }

    private static IReadOnlyDictionary<string, double> RegalNumericRatings(JsonElement ratings)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        if (ratings.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in ratings.EnumerateObject().Where(property => property.Value.ValueKind == JsonValueKind.Number))
        {
            result[property.Name] = property.Value.GetDouble();
        }

        return result;
    }

    private static IReadOnlyList<FleetPlannerPairing> RegalPairings(IEnumerable<JsonElement> candidates)
    {
        return candidates
            .Where(row => JsonString(row, "pairType") == "paired")
            .Select(row => new FleetPlannerPairing(
                Array(row, "pairs").Where(pair => pair.ValueKind == JsonValueKind.Number)
                    .Select(pair => pair.GetInt32()).ToList(),
                RegalTextRatings(Child(row, "ratings"))))
            .Where(pairing => pairing.PartnerGroups.Count > 0)
            .ToList();
    }
}
