using System.Globalization;
using System.Text.RegularExpressions;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    private const int SkillFireRateMaxLevel = 10;
    private const int SkillBuffScanDepthLimit = 12;
    private const double SkillAttrRatioScale = 10000.0;
    private static readonly Regex SkillBuffFileNameRegex =
        new(@"^buff_(\d+)\.lua$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SkillBuffIdRegex =
        new(@"\bbuff_id\s*=\s*(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SkillBuffIconRegex =
        new(@"\bicon\s*=\s*(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SkillBuffDisplayIdRegex =
        new(@"\bdisplay_id\s*=\s*(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SkillBuffEffectArgsRegex =
        new(
            "type\\s*=\\s*\"(?<type>BattleBuffAddAttrRatio|BattleBuffAddReloadRequirement)\"[\\s\\S]*?arg_list\\s*=\\s*\\{(?<args>[\\s\\S]*?)\\n\\s*\\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SkillBuffLoadSpeedAttrRegex =
        new(@"\battr\s*=\s*""loadSpeed""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private sealed record SkillLuaBuff(int Id, string Text, int IconId, int DisplayId, IReadOnlyList<int> BuffIds);

    private sealed record SkillReloadRequirementDelta(int WeaponType, double[] DeltaByLevel);

    private sealed record SkillFireRateAdjustment(
        double[] LoadSpeedRatioByLevel,
        IReadOnlyDictionary<int, double[]> WeaponReloadRequirementDeltaByType);

    private static Dictionary<string, Dictionary<string, object?>> BuildSkillMap(
        IReadOnlyList<Dictionary<string, object?>> skills,
        string buffDirectory)
    {
        var fireRateAdjustments = BuildSkillFireRateAdjustments(skills, buffDirectory);
        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var skill in skills)
        {
            var skillId = SafeInt(Get(skill, "id"));
            if (skillId == 0)
            {
                continue;
            }

            var row = new Dictionary<string, object?>
            {
                ["id"] = skillId,
                ["name"] = Get(skill, "name") ?? "",
                ["type"] = Get(skill, "type") ?? "",
                ["max_level"] = SafeInt(Get(skill, "max_level")),
                ["icon_url"] = ""
            };
            if (fireRateAdjustments.TryGetValue(skillId, out var fireRateAdjustment))
            {
                row["fire_rate"] = BuildSkillFireRateMap(fireRateAdjustment);
            }

            result[skillId.ToString(CultureInfo.InvariantCulture)] = row;
        }

        return result;
    }

    private static Dictionary<int, SkillFireRateAdjustment> BuildSkillFireRateAdjustments(
        IReadOnlyList<Dictionary<string, object?>> skills,
        string buffDirectory)
    {
        if (!Directory.Exists(buffDirectory))
        {
            return new Dictionary<int, SkillFireRateAdjustment>();
        }

        var buffs = LoadSkillLuaBuffs(buffDirectory);
        var ownerIndex = BuildSkillBuffOwnerIndex(buffs.Values);
        var result = new Dictionary<int, SkillFireRateAdjustment>();
        foreach (var skill in skills)
        {
            var skillId = SafeInt(Get(skill, "id"));
            if (skillId == 0)
            {
                continue;
            }

            var maxLevel = Math.Clamp(
                FirstPositive(SafeInt(Get(skill, "max_level")), SkillFireRateMaxLevel),
                1,
                SkillFireRateMaxLevel);
            var buffIds = CollectSkillBuffIds(skillId, buffs, ownerIndex);
            if (buffIds.Count == 0)
            {
                continue;
            }

            var adjustment = BuildSkillFireRateAdjustment(buffIds, buffs, maxLevel);
            if (adjustment is not null)
            {
                result[skillId] = adjustment;
            }
        }

        return result;
    }

    private static Dictionary<int, SkillLuaBuff> LoadSkillLuaBuffs(string buffDirectory)
    {
        var result = new Dictionary<int, SkillLuaBuff>();
        foreach (var path in Directory.EnumerateFiles(buffDirectory, "buff_*.lua", SearchOption.TopDirectoryOnly))
        {
            var match = SkillBuffFileNameRegex.Match(Path.GetFileName(path));
            if (!match.Success)
            {
                continue;
            }

            var id = SafeInt(match.Groups[1].Value);
            if (id == 0)
            {
                continue;
            }

            var text = File.ReadAllText(path);
            result[id] = new SkillLuaBuff(
                id,
                text,
                FirstRegexInt(text, SkillBuffIconRegex),
                FirstRegexInt(text, SkillBuffDisplayIdRegex),
                SkillBuffIdRegex.Matches(text)
                    .Select(item => SafeInt(item.Groups[1].Value))
                    .Where(item => item > 0)
                    .Distinct()
                    .ToArray());
        }

        return result;
    }

    private static Dictionary<int, HashSet<int>> BuildSkillBuffOwnerIndex(IEnumerable<SkillLuaBuff> buffs)
    {
        var result = new Dictionary<int, HashSet<int>>();
        foreach (var buff in buffs)
        {
            AddSkillBuffOwner(result, buff.IconId, buff.Id);
            AddSkillBuffOwner(result, buff.DisplayId, buff.Id);
        }

        return result;
    }

    private static void AddSkillBuffOwner(Dictionary<int, HashSet<int>> ownerIndex, int ownerId, int buffId)
    {
        if (ownerId <= 0 || buffId <= 0)
        {
            return;
        }

        if (!ownerIndex.TryGetValue(ownerId, out var buffIds))
        {
            buffIds = [];
            ownerIndex[ownerId] = buffIds;
        }

        buffIds.Add(buffId);
    }

    private static HashSet<int> CollectSkillBuffIds(
        int skillId,
        IReadOnlyDictionary<int, SkillLuaBuff> buffs,
        IReadOnlyDictionary<int, HashSet<int>> ownerIndex)
    {
        var result = new HashSet<int>();
        var pending = new Queue<(int BuffId, int Depth)>();
        pending.Enqueue((skillId, 0));
        if (ownerIndex.TryGetValue(skillId, out var ownedBuffIds))
        {
            foreach (var buffId in ownedBuffIds)
            {
                pending.Enqueue((buffId, 0));
            }
        }

        while (pending.Count > 0)
        {
            var (buffId, depth) = pending.Dequeue();
            if (!result.Add(buffId) || !buffs.TryGetValue(buffId, out var buff) || depth >= SkillBuffScanDepthLimit)
            {
                continue;
            }

            foreach (var childId in buff.BuffIds)
            {
                pending.Enqueue((childId, depth + 1));
            }
        }

        result.RemoveWhere(buffId => !buffs.ContainsKey(buffId));
        return result;
    }

    private static SkillFireRateAdjustment? BuildSkillFireRateAdjustment(
        IEnumerable<int> buffIds,
        IReadOnlyDictionary<int, SkillLuaBuff> buffs,
        int maxLevel)
    {
        var loadSpeedRatios = new double[maxLevel];
        var requirementDeltas = new Dictionary<int, double[]>();
        var hasLoadSpeedRatio = false;

        foreach (var buffId in buffIds.Order())
        {
            if (!buffs.TryGetValue(buffId, out var buff))
            {
                continue;
            }

            var loadSpeedRatioByLevel = ExtractLoadSpeedRatioByLevel(buff.Text, maxLevel);
            if (loadSpeedRatioByLevel.Length > 0)
            {
                AddLeveledValues(loadSpeedRatios, loadSpeedRatioByLevel);
                hasLoadSpeedRatio = true;
            }

            foreach (var delta in ExtractReloadRequirementDeltas(buff.Text, maxLevel))
            {
                if (!requirementDeltas.TryGetValue(delta.WeaponType, out var target))
                {
                    target = new double[maxLevel];
                    requirementDeltas[delta.WeaponType] = target;
                }

                AddLeveledValues(target, delta.DeltaByLevel);
            }
        }

        requirementDeltas = requirementDeltas
            .Where(item => item.Value.Any(HasMeaningfulSkillNumber))
            .ToDictionary(item => item.Key, item => RoundedSkillValues(item.Value));

        if (!hasLoadSpeedRatio && requirementDeltas.Count == 0)
        {
            return null;
        }

        return new SkillFireRateAdjustment(RoundedSkillValues(loadSpeedRatios), requirementDeltas);
    }

    private static Dictionary<string, object?> BuildSkillFireRateMap(SkillFireRateAdjustment adjustment)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (adjustment.LoadSpeedRatioByLevel.Any(HasMeaningfulSkillNumber))
        {
            result["load_speed_ratio_by_level"] = adjustment.LoadSpeedRatioByLevel;
        }

        var requirementDeltas = adjustment.WeaponReloadRequirementDeltaByType
            .OrderBy(item => item.Key)
            .Select(item => new Dictionary<string, object?>
            {
                ["weapon_type"] = item.Key,
                ["delta_by_level"] = item.Value
            })
            .ToArray();
        if (requirementDeltas.Length > 0)
        {
            result["weapon_reload_requirement_deltas"] = requirementDeltas;
        }

        return result;
    }

    private static double[] ExtractLoadSpeedRatioByLevel(string text, int maxLevel)
    {
        var levelValues = SkillBuffLevelBlocks(text)
            .Select(block => ExtractLoadSpeedRatio(block) / SkillAttrRatioScale)
            .ToArray();
        if (levelValues.Any(HasMeaningfulSkillNumber))
        {
            return NormalizeSkillLevelValues(levelValues, maxLevel);
        }

        var constantValue = ExtractLoadSpeedRatio(text) / SkillAttrRatioScale;
        return HasMeaningfulSkillNumber(constantValue)
            ? Enumerable.Repeat(constantValue, maxLevel).ToArray()
            : [];
    }

    private static double ExtractLoadSpeedRatio(string block)
    {
        return SkillBuffEffectArgsRegex.Matches(block)
            .Where(match => match.Groups["type"].Value == "BattleBuffAddAttrRatio")
            .Select(match => match.Groups["args"].Value)
            .Where(args => SkillBuffLoadSpeedAttrRegex.IsMatch(args))
            .Sum(args => LuaDoubleField(args, "number"));
    }

    private static SkillReloadRequirementDelta[] ExtractReloadRequirementDeltas(string text, int maxLevel)
    {
        var levelDeltaRows = SkillBuffLevelBlocks(text)
            .Select(ExtractReloadRequirementDeltaRow)
            .ToArray();
        var levelTypes = levelDeltaRows
            .SelectMany(row => row.Keys)
            .Distinct()
            .Order()
            .ToArray();
        if (levelTypes.Length > 0)
        {
            return levelTypes
                .Select(weaponType => new SkillReloadRequirementDelta(
                    weaponType,
                    NormalizeSkillLevelValues(
                        levelDeltaRows.Select(row => row.GetValueOrDefault(weaponType)).ToArray(),
                        maxLevel)))
                .ToArray();
        }

        var constantDeltas = ExtractReloadRequirementDeltaRow(text);
        return constantDeltas
            .OrderBy(item => item.Key)
            .Select(item => new SkillReloadRequirementDelta(
                item.Key,
                Enumerable.Repeat(item.Value, maxLevel).ToArray()))
            .ToArray();
    }

    private static Dictionary<int, double> ExtractReloadRequirementDeltaRow(string block)
    {
        var result = new Dictionary<int, double>();
        foreach (var args in SkillBuffEffectArgsRegex.Matches(block)
                     .Where(match => match.Groups["type"].Value == "BattleBuffAddReloadRequirement")
                     .Select(match => match.Groups["args"].Value))
        {
            var weaponType = (int)Math.Round(LuaDoubleField(args, "type"), MidpointRounding.AwayFromZero);
            var number = LuaDoubleField(args, "number");
            if (weaponType <= 0 || !HasMeaningfulSkillNumber(number))
            {
                continue;
            }

            result[weaponType] = result.GetValueOrDefault(weaponType) + number;
        }

        return result;
    }

    private static string[] SkillBuffLevelBlocks(string text)
    {
        var openIndex = text.IndexOf('{', StringComparison.Ordinal);
        if (openIndex < 0)
        {
            return [];
        }

        var closeIndex = FindLuaTableEnd(text, openIndex);
        var result = new List<string>();
        for (var index = openIndex + 1; index < closeIndex;)
        {
            var character = text[index];
            if (char.IsWhiteSpace(character) || character == ',')
            {
                index++;
                continue;
            }

            if (character == '{')
            {
                var levelCloseIndex = FindLuaTableEnd(text, index);
                if (PreviousSignificantCharacter(text, index) != '=')
                {
                    result.Add(text.Substring(index, levelCloseIndex - index + 1));
                }

                index = levelCloseIndex + 1;
                continue;
            }

            index++;
        }

        return result.ToArray();
    }

    private static char PreviousSignificantCharacter(string text, int index)
    {
        for (var cursor = index - 1; cursor >= 0; cursor--)
        {
            if (!char.IsWhiteSpace(text[cursor]))
            {
                return text[cursor];
            }
        }

        return '\0';
    }

    private static double[] NormalizeSkillLevelValues(IReadOnlyList<double> values, int maxLevel)
    {
        if (values.Count == 0)
        {
            return [];
        }

        var result = new double[maxLevel];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = values[Math.Min(index, values.Count - 1)];
        }

        return result;
    }

    private static void AddLeveledValues(double[] target, IReadOnlyList<double> values)
    {
        for (var index = 0; index < target.Length; index++)
        {
            target[index] += values[Math.Min(index, values.Count - 1)];
        }
    }

    private static double[] RoundedSkillValues(IEnumerable<double> values)
    {
        return values.Select(value => Math.Round(value, 6, MidpointRounding.AwayFromZero)).ToArray();
    }

    private static bool HasMeaningfulSkillNumber(double value)
    {
        return Math.Abs(value) > 0.000001;
    }

    private static int FirstRegexInt(string text, Regex regex)
    {
        var match = regex.Match(text);
        return match.Success ? SafeInt(match.Groups[1].Value) : 0;
    }

    private static double LuaDoubleField(string text, string field)
    {
        var match = Regex.Match(
            text,
            $@"\b{Regex.Escape(field)}\s*=\s*(?<value>-?\d+(?:\.\d+)?)",
            RegexOptions.CultureInvariant);
        return match.Success &&
               double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                   out var value)
            ? value
            : 0.0;
    }

    private static Dictionary<string, Dictionary<string, object?>> BuildShipLevelMap(
        IReadOnlyList<Dictionary<string, object?>> shipLevels)
    {
        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var row in shipLevels)
        {
            var level = FirstPositive(SafeInt(Get(row, "level")), SafeInt(Get(row, "id")));
            if (level == 0)
            {
                continue;
            }

            var entry = new Dictionary<string, object?>
            {
                ["level"] = level,
                ["exp"] = SafeInt(Get(row, "exp")),
                ["exp_ur"] = SafeInt(FirstNonEmpty(Get(row, "exp_ur"), Get(row, "exp"))),
                ["level_limit"] = SafeInt(Get(row, "level_limit"))
            };
            var awaken = BuildAwakeningCost(row);
            if (awaken.Count > 0)
            {
                entry["awaken"] = awaken;
            }

            result[level.ToString(CultureInfo.InvariantCulture)] = entry;
        }

        return result;
    }

    // Cost of the cognitive awakening that lifts the cap at this level, keyed by rarity code.
    // Each cost names "gold" (resource type 1, id 1) and items by id, e.g. 15008 성정 유닛, 15012 성정 유닛II.
    private static SortedDictionary<string, SortedDictionary<string, int>> BuildAwakeningCost(
        IReadOnlyDictionary<string, object?> row)
    {
        var result = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        for (var rarity = 2; rarity <= 6; rarity++)
        {
            if (Get(row, $"need_item_rarity{rarity}") is not List<int[]> items)
            {
                continue;
            }

            var cost = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in items.Where(item => item.Length >= 3))
            {
                var key = item[0] == 1 && item[1] == 1 ? "gold" : item[1].ToString(CultureInfo.InvariantCulture);
                cost[key] = cost.GetValueOrDefault(key) + item[2];
            }

            if (cost.Count > 0)
            {
                result[rarity.ToString(CultureInfo.InvariantCulture)] = cost;
            }
        }

        return result;
    }

    private static Dictionary<string, Dictionary<string, object?>> BuildStageMap(
        IReadOnlyList<Dictionary<string, object?>> stages,
        IReadOnlyList<Dictionary<string, object?>> chapters)
    {
        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var row in stages)
        {
            var stageId = SafeInt(Get(row, "id"));
            if (stageId == 0)
            {
                continue;
            }

            var name = Convert.ToString(Get(row, "name"), CultureInfo.InvariantCulture)?.Trim() ?? "";
            result[stageId.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["id"] = stageId,
                ["name"] = string.IsNullOrWhiteSpace(name) ? $"stage:{stageId}" : name,
                ["type"] = SafeInt(Get(row, "type")),
                ["dungeon_id"] = SafeInt(Get(row, "dungeon_id")),
                ["level"] = SafeInt(Get(row, "level")),
                // Base EXP per ship before rank, morale, flagship and MVP multipliers.
                ["exp"] = SafeInt(Get(row, "exp")),
                ["difficulty"] = SafeInt(Get(row, "difficulty")),
                ["icon"] = Get(row, "icon") ?? ""
            };
        }

        // Most specific first: an event reuses fleets across its chapters, and a fleet belongs to the chapter
        // where it is the boss or an elite rather than to one that merely lists it as a random encounter.
        // Between equally specific lists the later chapter wins, as it always has (reruns come later).
        var chapterStageFields = new[]
        {
            "boss_expedition_id",
            "elite_expedition_list",
            // Event chapters list their roaming elite fleets here; without it those battles had no chapter.
            "ai_expedition_list",
            "guarder_expedition_list",
            "ambush_expedition_list",
            "expedition_id_weight_list"
        };
        var assignedRank = new Dictionary<int, int>();
        foreach (var chapter in chapters)
        {
            var chapterId = SafeInt(Get(chapter, "id"));
            if (chapterId == 0)
            {
                continue;
            }

            var chapterName = Convert.ToString(Get(chapter, "chapter_name"), CultureInfo.InvariantCulture)?.Trim() ??
                              "";
            var chapterTitle = Convert.ToString(Get(chapter, "name"), CultureInfo.InvariantCulture)?.Trim() ?? "";
            var chapterLabel = string.Join(" ",
                new[] { chapterName, chapterTitle }.Where(item => !string.IsNullOrWhiteSpace(item))).Trim();
            var info = new Dictionary<string, object?>
            {
                ["chapter_id"] = chapterId,
                ["chapter_name"] = chapterName,
                ["chapter_title"] = chapterTitle,
                ["chapter_label"] = string.IsNullOrWhiteSpace(chapterLabel) ? $"chapter:{chapterId}" : chapterLabel,
                ["map"] = SafeInt(Get(chapter, "map")),
                ["chapter_type"] = SafeInt(Get(chapter, "type")),
                ["chapter_difficulty"] = SafeInt(Get(chapter, "difficulty"))
            };

            MergeStageInfo(result, chapterId, info, chapterLabel);
            // A chapter's own id stays its own even when another chapter of the event lists it.
            assignedRank[chapterId] = -1;
            for (var rank = 0; rank < chapterStageFields.Length; rank++)
            {
                // Weighted lists hold {stage id, weight, 0}; stage ids are the only values this large.
                foreach (var value in IntArray(Get(chapter, chapterStageFields[rank])))
                {
                    if (value >= 100000 && (!assignedRank.TryGetValue(value, out var current) || rank <= current))
                    {
                        assignedRank[value] = rank;
                        MergeStageInfo(result, value, info, "");
                    }
                }
            }
        }

        return result;
    }

    private static Dictionary<string, Dictionary<string, object?>> BuildFleetTechMap(
        IReadOnlyList<Dictionary<string, object?>> fleetTech)
    {
        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var record in fleetTech)
        {
            var techId = SafeInt(Get(record, "id"));
            if (techId == 0)
            {
                continue;
            }

            result[techId.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["id"] = techId,
                ["class_id"] = SafeInt(Get(record, "class")),
                ["max_star"] = SafeInt(Get(record, "max_star")),
                ["pt_get"] = SafeInt(Get(record, "pt_get")),
                ["pt_level"] = SafeInt(Get(record, "pt_level")),
                ["pt_upgrade"] = SafeInt(Get(record, "pt_upgrage")),
                ["get_attr"] = SafeInt(Get(record, "add_get_attr")),
                ["get_shiptypes"] = IntArray(Get(record, "add_get_shiptype")),
                ["get_value"] = SafeInt(Get(record, "add_get_value")),
                ["level_attr"] = SafeInt(Get(record, "add_level_attr")),
                ["level_shiptypes"] = IntArray(Get(record, "add_level_shiptype")),
                ["level_value"] = SafeInt(Get(record, "add_level_value"))
            };
        }

        return result;
    }

    private static Dictionary<string, Dictionary<string, object?>> BuildFleetTechNationMap(
        IReadOnlyList<Dictionary<string, object?>> fleetTech)
    {
        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var record in fleetTech)
        {
            var techId = SafeInt(Get(record, "id"));
            if (techId == 0)
            {
                continue;
            }

            result[techId.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["id"] = techId,
                ["group_id"] = SafeInt(Get(record, "groupid")),
                ["pt"] = SafeInt(Get(record, "pt")),
                ["cost"] = SafeInt(Get(record, "cost")),
                ["time"] = SafeInt(Get(record, "time")),
                ["add"] = Get(record, "add") ?? Array.Empty<object>()
            };
        }

        return result;
    }

    private static Dictionary<string, Dictionary<string, object?>> BuildShipBlueprintMap(
        IReadOnlyList<Dictionary<string, object?>> shipBlueprints)
    {
        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var record in shipBlueprints)
        {
            var blueprintId = SafeInt(Get(record, "id"));
            if (blueprintId == 0)
            {
                continue;
            }

            result[blueprintId.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["id"] = blueprintId,
                ["blueprint_version"] = SafeInt(Get(record, "blueprint_version")),
                ["is_pursuing"] = SafeInt(Get(record, "is_pursuing")),
                ["strengthen_item"] = SafeInt(Get(record, "strengthen_item"))
            };
        }

        return result;
    }

    private static Dictionary<string, Dictionary<string, object?>> BuildShipTransformMap(
        IReadOnlyList<Dictionary<string, object?>> shipTransforms)
    {
        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var record in shipTransforms)
        {
            var groupId = FirstPositive(SafeInt(Get(record, "group_id")), SafeInt(Get(record, "id")));
            if (groupId == 0)
            {
                continue;
            }

            var transformIds = IntArray(Get(record, "transform_ids")).Where(item => item != 0).ToArray();
            result[groupId.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["group_id"] = groupId,
                ["skill_id"] = SafeInt(Get(record, "skill_id")),
                ["skin_id"] = SafeInt(Get(record, "skin_id")),
                ["transform_ids"] = transformIds,
                ["required_count"] = transformIds.Length
            };
        }

        return result;
    }

    // Resources whose item name is shared or missing: paid and free gems (Player.chargeGem / awardGem), the mailbox
    // stock of gold and oil, and the production fields the commission screen shows waiting to be collected.
    private static readonly Dictionary<int, (string Label, string Icon)> ResourceLabelOverrides = new()
    {
        [4] = ("다이아(유료)", ""),
        [14] = ("다이아(무료)", ""),
        [16] = ("물자(우편함)", ""),
        [17] = ("연료(우편함)", ""),
        [5] = ("연료 생산(수령 대기)", "Props/oil"),
        [7] = ("물자 생산(수령 대기)", "Props/gold"),
        [10] = ("경험치(수령 대기)", "Props/exp")
    };

    /// <summary>
    /// player_resource with the name, icon and rarity of the item each resource stands for
    /// (item_virtual_data_statistics, else item_data_statistics). Icons in Props/ resolve to the extracted
    /// props AssetBundles under static/game-assets/props.
    /// </summary>
    private static Dictionary<string, Dictionary<string, object?>> BuildResourceMap(
        IReadOnlyList<Dictionary<string, object?>> resources, string luaDirectory)
    {
        var items = new Dictionary<int, Dictionary<string, object?>>();
        foreach (var (folder, table) in new[] { ("sharecfgdata", "item_data_statistics"), ("sharecfgdata", "item_virtual_data_statistics") })
        {
            foreach (var row in LoadLuaRecords(Path.Combine(luaDirectory, folder, table + ".lua"), table,
                         ["id", "name", "icon", "rarity"]))
            {
                if (SafeInt(Get(row, "id")) is > 0 and var id) items[id] = row;
            }
        }

        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var item in resources)
        {
            var resourceId = SafeInt(Get(item, "id"));
            if (resourceId == 0)
            {
                continue;
            }

            var itemId = SafeInt(FirstNonEmpty(Get(item, "itemid"), Get(item, "item_id")));
            items.TryGetValue(itemId, out var source);
            ResourceLabelOverrides.TryGetValue(resourceId, out var overrides);
            string Field(string key) => source is null ? "" : (Convert.ToString(Get(source, key), CultureInfo.InvariantCulture) ?? "").Trim();
            var icon = string.IsNullOrEmpty(overrides.Icon) ? Field("icon") : overrides.Icon;
            result[resourceId.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["id"] = resourceId,
                ["item_id"] = itemId,
                ["name"] = FirstNonEmpty(Get(item, "name"), $"resource:{resourceId}") ?? $"resource:{resourceId}",
                ["label"] = string.IsNullOrEmpty(overrides.Label) ? Field("name") : overrides.Label,
                ["icon_url"] = icon.StartsWith("Props/", StringComparison.OrdinalIgnoreCase)
                    ? $"/static/game-assets/props/{icon["Props/".Length..].ToLowerInvariant()}.png"
                    : "",
                ["rarity"] = source is null ? 1 : Math.Max(1, SafeInt(Get(source, "rarity")))
            };
        }

        return result;
    }

    private static void MergeStageInfo(
        IDictionary<string, Dictionary<string, object?>> result,
        int stageId,
        IReadOnlyDictionary<string, object?> info,
        string chapterLabel)
    {
        var key = stageId.ToString(CultureInfo.InvariantCulture);
        if (!result.TryGetValue(key, out var item))
        {
            item = new Dictionary<string, object?> { ["id"] = stageId };
            result[key] = item;
        }

        foreach (var (name, value) in info)
        {
            item[name] = value;
        }

        if (!string.IsNullOrWhiteSpace(chapterLabel))
        {
            item["name"] = chapterLabel;
        }
    }
}
