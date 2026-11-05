using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    // Standard leveling chapters offered next to the recent one (11-4 ... 15-4).
    private static readonly int[] StandardLevelingChapters = [1104, 1204, 1304, 1404, 1504];
    private const int RecentBattleWindow = 200;
    private const int FarmFleetSampleBattles = 20;
    private const int BossStageType = 99;

    private static LevelingShip ToLevelingShip(ShipState ship, FleetPlannerShip planner, SnapshotMaps maps)
    {
        var template = MapObject(maps.Ships, ship.TemplateId);
        return new LevelingShip
        {
            Uid = planner.Uid,
            Name = planner.Name,
            IconUrl = planner.IconUrl,
            Rarity = planner.Rarity,
            Hull = planner.Hull,
            Position = planner.Position,
            Level = ship.Level,
            Experience = ship.Exp,
            ExperienceRequired = ship.ExpRequired,
            ExperiencePercent = ship.ExpIsMax ? 100 : ship.ExpPercent,
            TotalExperience = ship.TotalExp,
            Morale = ship.Energy,
            IsLocked = ship.Locked,
            IsBulin = ship.IsBulin,
            TemplateMaxLevel = FirstPositive(JsonInt(template, "max_level"), 100),
            BestRatingScore = planner.Ratings.Values.Select(FleetPlanRating.Score).DefaultIfEmpty(0).Max(),
            MobRatingScore = Math.Max(FleetPlanRating.Score(planner.Ratings.GetValueOrDefault("W14 Mob")),
                FleetPlanRating.Score(planner.Ratings.GetValueOrDefault("W15/16 Mob"))),
            SelfSurvival = planner.Metrics.GetValueOrDefault("Self Survival"),
            AoeDamage = planner.Metrics.GetValueOrDefault("AoE Dmg")
        };
    }

    private static FleetPlannerInputs BuildFleetPlannerInputs(
        SqliteConnection connection,
        List<ShipState> ships,
        SnapshotMaps maps,
        IReadOnlyList<FleetPlannerShip> plannerShips,
        IReadOnlyList<LevelingShip> levelingShips,
        FleetPlanDashboard plan,
        IReadOnlyList<FleetCompositionGroupRow> groups,
        IReadOnlyList<EquipmentRow> equipment,
        string projectRoot)
    {
        var levelByUid = ships.Where(ship => ship.Uid.Length > 0)
            .GroupBy(ship => ship.Uid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Level, StringComparer.Ordinal);
        var battles = ReadLevelingBattles(connection);
        var stages = BuildLevelingStages(battles, maps.Stages, levelByUid, ships);
        var (farmFleetId, farmUids) = DetectFarmFleet(battles, stages.FirstOrDefault()?.Stage.Key, maps.Stages, groups);

        var bosses = MetaBossCatalog.Parse(maps.MetaBosses);
        var (guides, starRules) = MetaBossCatalog.ParseGuides(maps.MetaBossGuides);
        var current = MetaBossCatalog.Current(bosses, MetaBossCatalog.ServerTime(DateTimeOffset.Now));
        var guideUids = current is not null && guides.TryGetValue(current.Id, out var guide) && guide.Verified
            ? guide.Main.Concat(guide.Vanguard).SelectMany(slot => slot).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        // How many planner contents list each ship as a candidate, as the growth list shows it.
        var reach = plan.Contents
            .SelectMany(content => content.Candidates.Select(candidate => candidate.ShipUid).Distinct()
                .Select(uid => (Uid: uid, content.Label)))
            .GroupBy(entry => entry.Uid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Label).ToList(),
                StringComparer.Ordinal);
        var regalByUid = plannerShips.ToDictionary(ship => ship.Uid, ship => ship.RegalUid, StringComparer.Ordinal);
        var leveling = levelingShips.Select(ship => ship with
        {
            ContentReach = reach.GetValueOrDefault(ship.Uid)?.Count ?? 0,
            ContentLabels = reach.GetValueOrDefault(ship.Uid) ?? [],
            IsMetaGuideShip = guideUids.Contains(regalByUid.GetValueOrDefault(ship.Uid) ?? ""),
            FarmFleetId = farmUids.Contains(ship.Uid) ? farmFleetId : 0
        }).ToList();

        var gameFleets = groups
            .Select(group => (IReadOnlySet<string>)group.Ships.Select(row => row.ShipUid)
                .Where(uid => !string.IsNullOrWhiteSpace(uid)).ToHashSet(StringComparer.Ordinal))
            .Where(set => set.Count > 0)
            .ToList();
        var (gearShips, inventory, items) = BuildGearInputs(ships, plannerShips, maps, equipment, projectRoot);
        var metaFights = ReadMetaBossFights(connection, maps);
        return new FleetPlannerInputs
        {
            Ships = plannerShips,
            LevelingShips = leveling,
            Stages = stages,
            FarmFleetId = farmFleetId,
            FarmFleetUids = farmUids.ToList(),
            MetaBosses = bosses,
            MetaBossGuides = guides,
            StarRules = starRules,
            GameArchiveBossId = ReadGameArchiveBossId(connection),
            GameFleets = gameFleets,
            MetaFights = metaFights,
            MetaFightShips = MetaFightShips(metaFights, ships, plannerShips),
            GearMap =maps.RegalGearMap.ValueKind == JsonValueKind.Object ? new RegalGearMap(maps.RegalGearMap) : null,
            GearInventory = inventory,
            GearShips = gearShips,
            GearItems = items
        };
    }

    private sealed record LevelingBattle(int RowId, int StageId, int Score, IReadOnlyList<string> ShipUids);

    /// <summary>
    /// Battle results (SC 40004) paired with the request that reported their rank (CS 40003 score,
    /// 4 = S); the current-state tables keep the result but not the rank.
    /// </summary>
    private static List<LevelingBattle> ReadLevelingBattles(SqliteConnection connection)
    {
        var battles = new List<LevelingBattle>();
        if (!TableExists(connection, "decoded_packets"))
        {
            return battles;
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT rowid, packet_id, json
                              FROM decoded_packets
                              WHERE packet_id IN (40003, 40004)
                              ORDER BY rowid
                              """;
        using var reader = command.ExecuteReader();
        (int Stage, int Score)? pending = null;
        while (reader.Read())
        {
            JsonElement json;
            try
            {
                using var document = JsonDocument.Parse(reader.GetString(2));
                json = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }

            if (reader.GetInt32(1) == 40003)
            {
                pending = (JsonInt(json, "data"), json.TryGetProperty("score", out var score) ? IntValue(score) : -1);
                continue;
            }

            var uids = Array(json, "ship_exp_list")
                .Select(row => ValueText(Child(row, "ship_id")))
                .Where(uid => uid.Length > 0 && uid != "0")
                .ToList();
            if (pending is { } request && uids.Count > 0)
            {
                battles.Add(new LevelingBattle(reader.GetInt32(0), request.Stage, request.Score, uids));
            }

            pending = null;
        }

        return battles;
    }

    private static List<LevelingStageOption> BuildLevelingStages(
        IReadOnlyList<LevelingBattle> battles,
        JsonElement stageMap,
        IReadOnlyDictionary<string, int> levelByUid,
        List<ShipState> ships)
    {
        var lockedLeeches = ships.Where(ship => ship.Locked && ship.Level is > 0 and < LevelingPlanBuilder.ReadyLevel)
            .Select(ship => ship.Uid).ToHashSet(StringComparer.Ordinal);
        var recent = battles.TakeLast(RecentBattleWindow)
            .Where(battle => battle.ShipUids.Any(lockedLeeches.Contains))
            .Select(battle => ChapterId(stageMap, battle.StageId))
            .Where(chapter => chapter > 0)
            .GroupBy(chapter => chapter)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();

        var options = new List<LevelingStageOption>();
        foreach (var chapter in new[] { recent }.Where(id => id > 0).Concat(StandardLevelingChapters).Distinct())
        {
            var fought = battles.Where(battle => ChapterId(stageMap, battle.StageId) == chapter).ToList();
            var stage = RepresentativeStage(stageMap, chapter, fought);
            if (stage.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var ranked = fought.Where(battle => battle.Score >= 0).ToList();
            var records = ranked.Select(battle =>
            {
                var levels = battle.ShipUids.Select(uid => levelByUid.GetValueOrDefault(uid)).Where(level => level > 0).ToList();
                return new LevelingBattleRecord(levels.DefaultIfEmpty(0).Min(),
                    levels.Count(level => level >= LevelingPlanBuilder.ReadyLevel), battle.Score == 4);
            }).ToList();
            var chapterInfo = MapObject(stageMap, chapter);
            var label = FirstNonEmpty(JsonString(chapterInfo, "chapter_name"), JsonString(stage, "chapter_name"),
                chapter.ToString(CultureInfo.InvariantCulture)).Replace('–', '-');
            var title = FirstNonEmpty(JsonString(chapterInfo, "chapter_label"), JsonString(stage, "chapter_label"), label)
                .Replace('–', '-');
            var note = chapter == recent ? "최근 쫄작 해역"
                : chapter == 1204 ? "커뮤니티 기준 해역"
                : "";
            if (JsonInt(stage, "chapter_type") != 1 && chapter == recent) note = "이벤트 · 최근 쫄작 해역";
            options.Add(new LevelingStageOption(new LevelingStage
            {
                Key = chapter.ToString(CultureInfo.InvariantCulture),
                Label = label,
                Title = title,
                Note = note,
                EnemyLevel = JsonInt(stage, "level"),
                LuaExp = JsonInt(stage, "exp"),
                RecordBattles = ranked.Count,
                RecordSRankBattles = ranked.Count(battle => battle.Score == 4)
            }, records));
        }

        return options;
    }

    private static int ChapterId(JsonElement stageMap, int stageId) =>
        stageId <= 0 ? 0 : JsonInt(MapObject(stageMap, stageId), "chapter_id");

    // The enemy fleet farmed most on this chapter, else the chapter's usual mob EXP (its medium fleets).
    private static JsonElement RepresentativeStage(JsonElement stageMap, int chapter, IReadOnlyList<LevelingBattle> fought)
    {
        var mostFought = fought
            .Select(battle => MapObject(stageMap, battle.StageId))
            .Where(stage => stage.ValueKind == JsonValueKind.Object && JsonInt(stage, "type") != BossStageType &&
                            JsonInt(stage, "exp") > 0)
            .GroupBy(stage => JsonInt(stage, "id"))
            .OrderByDescending(group => group.Count())
            .Select(group => group.First())
            .FirstOrDefault();
        if (mostFought.ValueKind == JsonValueKind.Object)
        {
            return mostFought;
        }

        if (stageMap.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        var mobs = stageMap.EnumerateObject().Select(property => property.Value)
            .Where(stage => stage.ValueKind == JsonValueKind.Object && JsonInt(stage, "chapter_id") == chapter &&
                            JsonInt(stage, "id") != chapter && JsonInt(stage, "type") != BossStageType &&
                            JsonInt(stage, "exp") > 0)
            .ToList();
        return mobs.GroupBy(stage => JsonInt(stage, "exp"))
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key)
            .Select(group => group.First())
            .FirstOrDefault();
    }

    // The sortie fleet that shares the most ships with the latest battles on the recent leveling chapter.
    private static (int FleetId, IReadOnlySet<string> Uids) DetectFarmFleet(
        IReadOnlyList<LevelingBattle> battles, string? recentChapterKey, JsonElement stageMap,
        IReadOnlyList<FleetCompositionGroupRow> groups)
    {
        var empty = (0, (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal));
        if (!int.TryParse(recentChapterKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chapter))
        {
            return empty;
        }

        var fought = battles.Where(battle => ChapterId(stageMap, battle.StageId) == chapter)
            .TakeLast(FarmFleetSampleBattles)
            .SelectMany(battle => battle.ShipUids)
            .ToHashSet(StringComparer.Ordinal);
        var best = groups
            .Where(group => group.FleetId is >= 1 and <= 6)
            .Select(group => (group.FleetId, Uids: group.Ships.Select(row => row.ShipUid)
                .Where(uid => !string.IsNullOrWhiteSpace(uid)).ToHashSet(StringComparer.Ordinal)))
            .Select(fleet => (fleet.FleetId, fleet.Uids, Overlap: fleet.Uids.Count(fought.Contains)))
            .OrderByDescending(fleet => fleet.Overlap)
            .ThenBy(fleet => fleet.FleetId)
            .FirstOrDefault();
        return best.Overlap >= 2 ? (best.FleetId, best.Uids) : empty;
    }

    private static int ReadGameArchiveBossId(SqliteConnection connection)
    {
        if (!TableExists(connection, "decoded_packets"))
        {
            return 0;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM decoded_packets WHERE packet_id = 34502 ORDER BY rowid DESC LIMIT 1";
        if (command.ExecuteScalar() is not string json)
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonInt(document.RootElement, "default_boss_id");
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static (IReadOnlyDictionary<string, GearFleetShip> Ships, GearInventory Inventory,
        IReadOnlyDictionary<int, GearItemInfo> Items) BuildGearInputs(
            List<ShipState> ships,
            IReadOnlyList<FleetPlannerShip> plannerShips,
            SnapshotMaps maps,
            IReadOnlyList<EquipmentRow> equipment,
            string projectRoot)
    {
        // Enhanced pieces have their own template ids; Regal ranks the base piece.
        int Base(int templateId) => FirstPositive(JsonInt(MapObject(maps.Equipment, templateId), "base"), templateId);

        var gearIdByUid = plannerShips.ToDictionary(ship => ship.Uid, ship => ship.RegalGearId, StringComparer.Ordinal);
        var gearShips = new Dictionary<string, GearFleetShip>(StringComparer.Ordinal);
        var holders = new List<GearHolder>();
        var baseIds = new HashSet<int>();
        foreach (var ship in ships.Where(ship => ship.Uid.Length > 0))
        {
            var current = new Dictionary<int, GearItemCopy>();
            foreach (var item in ship.Equipment.Where(item => !item.Empty && item.TemplateId > 0))
            {
                var copy = new GearItemCopy(Base(item.TemplateId), item.EnhanceLevel);
                current[item.Slot] = copy;
                baseIds.Add(copy.BaseId);
                holders.Add(new GearHolder(ship.Uid, ship.Name.Trim(), item.Slot, copy.BaseId, copy.Enhance,
                    ship.Level, ship.FleetId > 0));
            }

            if (gearIdByUid.TryGetValue(ship.Uid, out var gearId))
            {
                gearShips[ship.Uid] = new GearFleetShip(ship.Uid, ship.Name.Trim(), gearId, false, current);
            }
        }

        var spare = new Dictionary<int, List<int>>();
        foreach (var row in equipment.Where(row => row.BagCount > 0))
        {
            var templateId = ToInt(row.TemplateId);
            if (templateId <= 0) continue;
            var baseId = Base(templateId);
            baseIds.Add(baseId);
            if (!spare.TryGetValue(baseId, out var copies)) spare[baseId] = copies = [];
            copies.AddRange(Enumerable.Repeat(row.EnhanceLevel, row.BagCount));
        }

        foreach (var entry in EnumerateGearMapIds(maps.RegalGearMap))
        {
            baseIds.Add(entry);
        }

        var items = new Dictionary<int, GearItemInfo>();
        foreach (var baseId in baseIds)
        {
            var info = MapObject(maps.Equipment, baseId);
            items[baseId] = new GearItemInfo(baseId,
                FirstNonEmpty(JsonString(info, "name"), $"equip:{baseId}"),
                Asset(JsonString(info, "icon_url"), projectRoot),
                RarityTier(JsonInt(info, "rarity")),
                EquipmentTypeGroup(JsonInt(info, "type")));
        }

        return (gearShips,
            new GearInventory
            {
                Spare = spare.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<int>)pair.Value),
                Equipped = holders
            },
            items);
    }

    private static IEnumerable<int> EnumerateGearMapIds(JsonElement gearMap)
    {
        var equips = Child(gearMap, "equip");
        if (equips.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in equips.EnumerateObject())
        {
            var entries = property.Value.ValueKind == JsonValueKind.Array
                ? property.Value.EnumerateArray().ToList()
                : [property.Value];
            foreach (var entry in entries)
            {
                var id = JsonInt(entry, "id");
                if (id > 0) yield return id;
            }
        }
    }
}
