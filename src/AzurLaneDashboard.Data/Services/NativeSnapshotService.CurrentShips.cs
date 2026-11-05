using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;
using Microsoft.Data.Sqlite;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private static List<ShipState> BuildCurrentShips(SqliteConnection connection, SnapshotMaps maps,
        string projectRoot)
    {
        var skillsByShip = ReadCurrentSkills(connection);
        var equipmentAttributes = LoadEquipmentAttributes(projectRoot);
        var slotsByShip = ReadCurrentEquipmentSlots(connection, maps.Equipment, maps.EquipmentSkins, projectRoot,
            equipmentAttributes);
        var specialByShip = ReadCurrentSpecialByShip(connection, maps.SpWeapons, projectRoot);
        var shipStats = LoadShipStats(projectRoot);
        var expGains = ReadExpSinceSnapshots(connection);
        var morale = ReadMoraleRecovery(connection);
        var levelCaps = new ShipLevelCaps(maps.ShipLevels);
        var rows = new List<ShipState>();

        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT *
                              FROM current_ships
                              ORDER BY level DESC, ship_uid
                              """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var uid = Text(reader, "ship_uid");
            var templateId = Int(reader, "ship_template_id");
            var shipInfo = MapObject(maps.Ships, templateId);
            var skinId = FirstPositive(Int(reader, "skin_id"), JsonInt(shipInfo, "skin_id"));
            var skinInfo = MapObject(maps.Skins, skinId);
            var baseRarity = JsonInt(shipInfo, "rarity");
            var raw = ParseJson(Text(reader, "raw_json"));
            var groupId = FirstPositive(JsonInt(shipInfo, "group_type"), templateId / 10);
            var transformIds = Array(raw, "transform_list")
                .Select(item => FirstPositive(JsonInt(item, "id"), IntValue(item)))
                .Where(id => id > 0)
                .ToList();
            var isRetrofitted = RetrofitComplete(transformIds, groupId, maps.ShipTransforms);
            var rarity = ShipDisplayRarity(
                baseRarity,
                templateId,
                groupId,
                maps.ShipBlueprints,
                isRetrofitted);
            var skills = ResolveSkillStates(skillsByShip.GetValueOrDefault(uid), raw);
            var knownSlots = slotsByShip.GetValueOrDefault(uid);
            var equipment = knownSlots ?? EmptyEquipmentSlots();
            var special = specialByShip.GetValueOrDefault(uid);
            shipStats.TryGetValue(templateId, out var stats);
            var name = FirstNonEmpty(JsonString(shipInfo, "name"), uid);
            var customName = FirstNonEmpty(Text(reader, "custom_name"), JsonString(raw, "custom_name"),
                CustomNameFromDisplayName(Text(reader, "display_name"), name));
            var fleetTech = ResolveFleetTech(templateId, shipInfo, skinId, maps.FleetTech);
            var fleetTechGetBonus = FleetBonusText(fleetTech, "get");
            var fleetTechLevelBonus = FleetBonusText(fleetTech, "level");
            var level = Int(reader, "level");
            var exp = Int(reader, "exp");
            var snapshotRowId = Int(reader, "snapshot_rowid");
            if (snapshotRowId > 0 && expGains.TryGetValue(uid, out var gains))
            {
                var templateMaxLevel = FirstPositive(JsonInt(shipInfo, "max_level"), 100);
                var maxLevel = PacketMaxLevel(raw);
                foreach (var gain in gains)
                {
                    if (gain.RowId <= snapshotRowId) continue;
                    if (gain.RaisesMaxLevel)
                    {
                        // UpgradeShipMaxLevelCommand: the next cap, then addExp(0, true) spends banked EXP.
                        maxLevel = levelCaps.NextCap(maxLevel > 0 ? maxLevel : levelCaps.MaxLevel(level, templateMaxLevel));
                        (level, exp) = levelCaps.AddExperience(level, exp, 0, rarity, templateMaxLevel, true, maxLevel);
                    }
                    else
                    {
                        (level, exp) = levelCaps.AddExperience(level, exp, gain.Exp, rarity, templateMaxLevel,
                            gain.BanksOverflow, maxLevel);
                    }
                }
            }

            var progress = LevelProgress(level, exp, rarity, maps.ShipLevels);
            var intimacy = FirstPositive(Int(reader, "intimacy"), JsonInt(raw, "intimacy"));
            var strength = ReadShipStrengthState(raw);
            var transformLevels = ReadTransformLevels(raw);
            var effectiveStats = EffectiveStats(stats, level, intimacy, strength, transformLevels, equipment);
            var enhancement = ShipEnhancement(
                stats,
                level,
                strength,
                snapshotRowId > 0 || strength.AttributeExp.Count > 0);

            rows.Add(new ShipState
            {
                Uid = uid,
                TemplateId = templateId,
                GroupId = groupId,
                Name = name,
                CustomName = customName,
                IconUrl = Asset(FirstNonEmpty(JsonString(skinInfo, "icon_url"),
                    JsonString(skinInfo, "shipyard_url"), JsonString(skinInfo, "qicon_url")), projectRoot),
                BackgroundUrl =
                    Asset(
                        FirstNonEmpty(JsonString(skinInfo, "shipyard_url"),
                            JsonString(skinInfo, "icon_url")), projectRoot),
                Rarity = rarity,
                RarityClass = RarityCssClass(rarity),
                RarityScore = RarityScore(rarity),
                Level = level,
                LimitBreak = FirstPositive(Int(reader, "limit_break"), JsonInt(shipInfo, "limit_break")),
                EnhanceLevel = enhancement.Value,
                EnhanceText = enhancement.Text,
                Exp = exp,
                ExpCurrent = progress.Current,
                ExpRequired = progress.Required,
                ExpPercent = progress.Percent,
                ExpIsMax = progress.IsMax,
                TotalExp = levelCaps.Total(level, exp, rarity),
                Energy = morale.Apply(Int(reader, "energy"), uid, JsonInt(raw, "propose") != 0),
                Intimacy = intimacy,
                CreateTime = Int(reader, "create_time"),
                StatsSummary = ShipStatsSummary(effectiveStats),
                StatsDetail = ShipStatsDetail(effectiveStats),
                StatRows = ShipStatRows(effectiveStats),
                Reload = effectiveStats?.Reload ?? 0,
                ShipTypeId = JsonInt(shipInfo, "ship_type"),
                NationId = JsonInt(shipInfo, "nationality"),
                DefaultEquipmentIds = stats?.DefaultEquipmentIds ?? [],
                DefaultEquipmentTypes = stats?.DefaultEquipmentTypes ?? [],
                AirSlotWeights = stats?.AirSlotWeights ?? [],
                SkinId = skinId,
                SkinName = JsonString(skinInfo, "name"),
                Locked = Int(reader, "locked") != 0,
                IsProposed = JsonInt(raw, "propose") != 0,
                Skills = skills,
                SkillSummary = skills.Count == 0 ? "-" : string.Join("/", skills.Select(skill => skill.Level)),
                SkillGap = SkillGap(skills, maps.Skills),
                Equipment = equipment,
                EquippedSummary = EquippedSummary(equipment),
                // The slot table mirrors the server's equip_info_list, so empty slots are counted from it.
                EmptySlots = knownSlots?.Count(item => item.Empty) ?? 0,
                SpecialEquipment = special,
                SpecialEquipmentSummary = special is null ? "" :
                    special.EnhanceLevel > 0 ? $"{special.Name} +{special.EnhanceLevel}" : special.Name,
                FleetTechGetBonus = fleetTechGetBonus,
                FleetTechLevelBonus = fleetTechLevelBonus,
                SnapshotRowId = snapshotRowId,
                HasFullState = snapshotRowId > 0,
                IsBulin = IsBulin(templateId, JsonString(shipInfo, "name"))
            });
        }

        ApplyFleetTechReloadAdditions(rows, maps.FleetTech);
        rows.Sort((a, b) => CompareTuple(b.Level, b.RarityScore, b.Name, a.Level, a.RarityScore, a.Name));
        return rows;
    }

    // EXP books (item_data_statistics usage_arg): 함선 연습 데이터 T1 and T2.
    private static readonly Dictionary<int, int> ExpBookExp = new() { [16501] = 3000, [16502] = 15000 };

    private readonly record struct ExpGain(long RowId, int Exp, bool BanksOverflow, bool RaisesMaxLevel = false);

    // Ship info field 19 (common_pb max_level). Rows decoded before it had that name kept it as "commanderid"
    // and stored field 20, a 0/1 flag, as max_level.
    private static int PacketMaxLevel(JsonElement raw) =>
        JsonInt(raw, "max_level") is > 1 and var maxLevel ? maxLevel : JsonInt(raw, "commanderid");

    /// <summary>
    /// EXP the client adds itself, captured after the oldest ship snapshot, per ship in packet order: battle
    /// results (SC 40004), EXP books (CS 22011 once SC 22012 succeeds), courtyard training (SC 19027 for the
    /// ships on floor 1, SC 19005 for a ship leaving) and commissions (SC 13006 for the ships sent), plus
    /// level-cap breaks (CS 12038 once SC 12039 succeeds). The server sends only the gain, so current_ships keeps
    /// the login level until the next full ship list; the snapshot replays the gains newer than each ship's row
    /// up to the ship's max level. Only battle EXP is banked at that level.
    /// </summary>
    private static Dictionary<string, List<ExpGain>> ReadExpSinceSnapshots(SqliteConnection connection)
    {
        var result = new Dictionary<string, List<ExpGain>>(StringComparer.Ordinal);
        if (!TableExists(connection, "decoded_packets") || !TableExists(connection, "current_ships"))
        {
            return result;
        }

        long since;
        long start;
        using (var command = connection.CreateCommand())
        {
            // Who trains in the courtyard and who went on which commission come from the login lists
            // (SC 19001, SC 13002) at or before the oldest snapshot, so the scan starts there.
            command.CommandText = """
                                  SELECT since, COALESCE(MIN(dorm, commissions), dorm, commissions, since)
                                  FROM (SELECT
                                      (SELECT COALESCE(MIN(snapshot_rowid), 0) FROM current_ships WHERE snapshot_rowid > 0) AS since,
                                      (SELECT MAX(rowid) FROM decoded_packets WHERE packet_id = 19001 AND rowid <=
                                          (SELECT COALESCE(MIN(snapshot_rowid), 0) FROM current_ships WHERE snapshot_rowid > 0)) AS dorm,
                                      (SELECT MAX(rowid) FROM decoded_packets WHERE packet_id = 13002 AND rowid <=
                                          (SELECT COALESCE(MIN(snapshot_rowid), 0) FROM current_ships WHERE snapshot_rowid > 0)) AS commissions)
                                  """;
            using var bounds = command.ExecuteReader();
            if (!bounds.Read() || bounds.GetInt64(0) <= 0) return result;
            since = bounds.GetInt64(0);
            start = Math.Min(since, bounds.GetInt64(1));
        }

        void Add(string uid, long rowId, int exp, bool banks)
        {
            if (rowId <= since || uid.Length == 0 || uid == "0" || exp <= 0) return;
            if (!result.TryGetValue(uid, out var gains)) result[uid] = gains = [];
            gains.Add(new ExpGain(rowId, exp, banks));
        }

        var training = new HashSet<string>(StringComparer.Ordinal);
        var commissions = new Dictionary<int, List<string>>();
        (string Ship, int Exp)? booking = null;
        (string Ship, int Floor)? placing = null;
        var leaving = "";
        var raising = "";
        (int Id, List<string> Ships)? sending = null;
        var finishing = 0;

        using var scan = connection.CreateCommand();
        scan.CommandText = """
                           SELECT rowid, packet_id, json
                           FROM decoded_packets
                           WHERE rowid >= $start AND packet_id IN
                               (40004, 22011, 22012, 12038, 12039, 19001, 19002, 19003, 19004, 19005, 19027, 13002, 13003, 13004, 13005, 13006)
                             AND (rowid > $since OR packet_id IN (19001, 19002, 19003, 19004, 19005, 13002, 13003, 13004))
                           ORDER BY rowid
                           """;
        scan.Parameters.AddWithValue("$start", start);
        scan.Parameters.AddWithValue("$since", since);
        using var reader = scan.ExecuteReader();
        while (reader.Read())
        {
            var rowId = reader.GetInt64(0);
            var json = ParseJson(reader.GetString(2));
            var ok = JsonInt(json, "result") == 0;
            switch (reader.GetInt32(1))
            {
                case 40004:
                    foreach (var item in Items(json, "ship_exp_list"))
                        Add(ValueText(Child(item, "ship_id")), rowId, JsonInt(item, "exp"), true);
                    break;
                case 12038:
                    raising = ValueText(Child(json, "ship_id"));
                    break;
                case 12039:
                    if (ok && raising.Length > 0 && raising != "0" && rowId > since)
                    {
                        if (!result.TryGetValue(raising, out var raises)) result[raising] = raises = [];
                        raises.Add(new ExpGain(rowId, 0, true, true));
                    }

                    raising = "";
                    break;
                case 22011:
                    booking = (ValueText(Child(json, "ship_id")),
                        Items(json, "books").Sum(book => ExpBookExp.GetValueOrDefault(JsonInt(book, "id")) * JsonInt(book, "num")));
                    break;
                case 22012:
                    if (ok && booking is { } book) Add(book.Ship, rowId, book.Exp, false);
                    booking = null;
                    break;
                case 19001:
                    training = TrainingShips(json).Select(id => id.ToString(CultureInfo.InvariantCulture))
                        .ToHashSet(StringComparer.Ordinal);
                    break;
                case 19002:
                    placing = (ValueText(Child(json, "ship_id")), JsonInt(json, "type"));
                    break;
                case 19003:
                    if (ok && placing is { } placed)
                    {
                        if (placed.Floor == DormTrainingFloor) training.Add(placed.Ship);
                        else training.Remove(placed.Ship);
                    }

                    placing = null;
                    break;
                case 19004:
                    leaving = ValueText(Child(json, "ship_id"));
                    break;
                case 19005:
                    if (ok)
                    {
                        Add(leaving, rowId, JsonInt(json, "exp"), false);
                        training.Remove(leaving);
                    }

                    break;
                case 19027:
                    foreach (var ship in training) Add(ship, rowId, JsonInt(json, "exp"), false);
                    break;
                case 13002:
                    commissions = [];
                    foreach (var item in Items(json, "collection_list"))
                        commissions[JsonInt(item, "id")] = Items(item, "ship_id_list").Select(ValueText).ToList();
                    break;
                case 13003:
                    sending = (JsonInt(json, "id"), Items(json, "ship_id_list").Select(ValueText).ToList());
                    break;
                case 13004:
                    if (ok && sending is { } sent) commissions[sent.Id] = sent.Ships;
                    sending = null;
                    break;
                case 13005:
                    finishing = JsonInt(json, "id");
                    break;
                case 13006:
                    if (ok && commissions.Remove(finishing, out var crew))
                    {
                        foreach (var ship in crew) Add(ship, rowId, JsonInt(json, "exp"), false);
                    }

                    break;
            }
        }

        return result;
    }

    private static Dictionary<string, List<SkillState>> ReadCurrentSkills(SqliteConnection connection)
    {
        var result = new Dictionary<string, List<SkillState>>(StringComparer.Ordinal);
        if (!TableExists(connection, "current_ship_skills"))
        {
            return result;
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT ship_uid, skill_id, skill_lv, skill_exp
                              FROM current_ship_skills
                              ORDER BY ship_uid, slot_index
                              """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var uid = Text(reader, "ship_uid");
            if (string.IsNullOrWhiteSpace(uid))
            {
                continue;
            }

            if (!result.TryGetValue(uid, out var skills))
            {
                skills = new List<SkillState>();
                result[uid] = skills;
            }

            var skill = new SkillState(Int(reader, "skill_id"), Int(reader, "skill_lv"), Int(reader, "skill_exp"));
            if (skill.Id > 0)
            {
                skills.Add(skill);
            }
        }

        return result;
    }

    private static List<SkillState> ResolveSkillStates(IReadOnlyList<SkillState>? skills, JsonElement raw)
    {
        var rawSkills = SkillStatesFromRaw(raw);
        if (skills is null || skills.Count == 0)
        {
            return rawSkills;
        }

        var result = skills.Where(skill => skill.Id > 0).ToList();
        var knownSkillIds = result.Select(skill => skill.Id).ToHashSet();
        foreach (var rawSkill in rawSkills)
        {
            if (knownSkillIds.Add(rawSkill.Id))
            {
                result.Add(rawSkill);
            }
        }

        return result.Count > 0 ? result : rawSkills;
    }

    private static List<SkillState> SkillStatesFromRaw(JsonElement raw)
    {
        return Array(raw, "skill_id_list")
            .Select(skill => new SkillState(
                JsonInt(skill, "skill_id"),
                JsonInt(skill, "skill_lv"),
                JsonInt(skill, "skill_exp")))
            .Where(skill => skill.Id > 0)
            .ToList();
    }

    private static Dictionary<string, List<EquipmentItemState>> ReadCurrentEquipmentSlots(
        SqliteConnection connection,
        JsonElement equipmentMap,
        JsonElement equipmentSkinMap,
        string projectRoot,
        IReadOnlyDictionary<int, IReadOnlyList<EquipmentAttributeRow>> equipmentAttributes)
    {
        var result = new Dictionary<string, List<EquipmentItemState>>(StringComparer.Ordinal);
        if (!TableExists(connection, "current_ship_equipment_slots"))
        {
            return result;
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT *
                              FROM current_ship_equipment_slots
                              ORDER BY ship_uid, slot
                              """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var uid = Text(reader, "ship_uid");
            var slot = Int(reader, "slot");
            if (string.IsNullOrWhiteSpace(uid) || slot <= 0)
            {
                continue;
            }

            if (!result.TryGetValue(uid, out var slots))
            {
                slots = EmptyEquipmentSlots();
                result[uid] = slots;
            }

            var templateId = Int(reader, "equipment_template_id");
            var info = MapObject(equipmentMap, templateId);
            var skinId = Int(reader, "skin_id");
            var skinInfo = MapObject(equipmentSkinMap, skinId);
            var skinIconUrl = Asset(JsonString(skinInfo, "icon_url"), projectRoot);
            var rarity = RarityTier(JsonInt(info, "rarity"));
            var enhanceLevel = FirstPositive(Int(reader, "enhance_level"),
                EquipmentEnhanceLevel(JsonInt(info, "enhance_level")));
            var attributes = EquipmentAttributes(info, templateId, equipmentAttributes);
            slots[slot - 1] = new EquipmentItemState
            {
                Slot = slot,
                Empty = Int(reader, "empty") != 0 || templateId <= 0,
                TemplateId = templateId,
                Name = FirstNonEmpty(JsonString(info, "name"), templateId > 0 ? $"equip:{templateId}" : "빈칸"),
                IconUrl = templateId > 0 ? Asset(JsonString(info, "icon_url"), projectRoot) : skinIconUrl,
                Rarity = rarity,
                RarityClass = $"rarity-{RarityFilterTier(rarity)}",
                EnhanceLevel = enhanceLevel,
                TypeGroup = EquipmentTypeGroup(JsonInt(info, "type")),
                Attributes = attributes,
                ReloadBonus = EquipmentReloadBonus(attributes),
                SkinId = skinId,
                SkinIconUrl = skinIconUrl,
                SkinName = JsonString(skinInfo, "name")
            };
        }

        return result;
    }

    private static List<EquipmentItemState> EmptyEquipmentSlots()
    {
        return Enumerable.Range(1, 5).Select(slot => new EquipmentItemState { Slot = slot, Empty = true }).ToList();
    }

    private static Dictionary<string, SpecialEquipmentRow> ReadCurrentSpecialByShip(
        SqliteConnection connection,
        JsonElement spweaponMap,
        string projectRoot)
    {
        var result = new Dictionary<string, SpecialEquipmentRow>(StringComparer.Ordinal);
        if (!TableExists(connection, "current_ship_special_equipment"))
        {
            return result;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM current_ship_special_equipment";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var uid = Text(reader, "ship_uid");
            var templateId = Int(reader, "template_id");
            if (string.IsNullOrWhiteSpace(uid) || templateId <= 0)
            {
                continue;
            }

            result[uid] = CurrentSpecialEquipmentRow(reader, spweaponMap, projectRoot);
        }

        return result;
    }
}
