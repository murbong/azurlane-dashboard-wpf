using System.Globalization;
using System.IO;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private const int DurabilityAttrIndex = 0;
    private const int CannonAttrIndex = 1;
    private const int TorpedoAttrIndex = 2;
    private const int AntiAircraftAttrIndex = 3;
    private const int AirAttrIndex = 4;
    private const int ReloadAttrIndex = 5;
    private const int HitAttrIndex = 7;
    private const int DodgeAttrIndex = 8;
    private const int SpeedAttrIndex = 9;
    private const int LuckAttrIndex = 10;
    private const int AntiSubAttrIndex = 11;
    private const int CannonStrengthIndex = 0;
    private const int TorpedoStrengthIndex = 1;
    private const int AntiAircraftStrengthIndex = 2;
    private const int AirStrengthIndex = 3;
    private const int ReloadAttributeTypeId = 6;
    private const int ReloadStrengthIndex = 4;
    private const int CannonStrengthAttrId = 2;
    private const int TorpedoStrengthAttrId = 3;
    private const int AntiAircraftStrengthAttrId = 4;
    private const int AirStrengthAttrId = 5;
    private const int ReloadStrengthAttrId = 6;
    private const int ExtraAttrLevelLimit = 100;
    private const int FleetTechLevelThreshold = 120;

    private static IReadOnlyDictionary<int, ShipStatBlock> LoadShipStats(string projectRoot)
    {
        var path = Path.Combine(projectRoot, "external", "AzurLaneLuaScripts", "kr", "sharecfgdata",
            "ship_data_statistics.lua");
        if (!File.Exists(path))
        {
            return new Dictionary<int, ShipStatBlock>();
        }

        var templatePath = Path.Combine(projectRoot, "external", "AzurLaneLuaScripts", "kr", "sharecfgdata",
            "ship_data_template.lua");
        var strengthenPath = Path.Combine(projectRoot, "external", "AzurLaneLuaScripts", "kr", "sharecfg",
            "ship_data_strengthen.lua");
        var blueprintPath = Path.Combine(projectRoot, "external", "AzurLaneLuaScripts", "kr", "sharecfg",
            "ship_data_blueprint.lua");
        var strengthenBlueprintPath = Path.Combine(projectRoot, "external", "AzurLaneLuaScripts", "kr", "sharecfg",
            "ship_strengthen_blueprint.lua");
        var equipmentPath = Path.Combine(projectRoot, "external", "AzurLaneLuaScripts", "kr", "sharecfgdata",
            "equip_data_statistics.lua");
        var transformTemplatePath = Path.Combine(projectRoot, "external", "AzurLaneLuaScripts", "kr", "sharecfg",
            "transform_data_template.lua");
        var signature =
            $"{FileSignature(path)}|{FileSignature(templatePath)}|{FileSignature(strengthenPath)}|" +
            $"{FileSignature(blueprintPath)}|{FileSignature(strengthenBlueprintPath)}|{FileSignature(equipmentPath)}|" +
            $"{FileSignature(transformTemplatePath)}";
        lock (ShipStatsCacheLock)
        {
            if (ShipStatsCache.TryGetValue(path, out var cached) && cached.Signature == signature)
            {
                return cached.Stats;
            }
        }

        var strengthenIds = LoadStrengthenIds(templatePath);
        var oilConsumption = LoadOilConsumption(templatePath);
        var strengthenStats = LoadStrengthenStats(strengthenPath);
        var blueprintStats = LoadBlueprintStats(blueprintPath, strengthenBlueprintPath);
        var equipmentTypes = LoadEquipmentTypes(equipmentPath);
        var transformReloadEffects = LoadTransformReloadEffects(transformTemplatePath);
        var stats = new Dictionary<int, ShipStatBlock>();
        var currentId = 0;
        var armorType = 0;
        List<double>? readingValues = null;
        var attrs = new List<double>();
        var growth = new List<double>();
        var growthExtra = new List<double>();
        var baseList = new List<double>();
        var defaultEquipList = new List<double>();

        void StoreCurrent()
        {
            if (currentId <= 0 || attrs.Count < 12 || stats.ContainsKey(currentId))
            {
                currentId = 0;
                return;
            }

            int Attr(int index) => index < attrs.Count ? (int)Math.Floor(attrs[index]) : 0;
            int Growth(int index) => index < growth.Count ? (int)Math.Floor(growth[index]) : 0;
            int Extra(int index) => index < growthExtra.Count ? (int)Math.Floor(growthExtra[index]) : 0;
            var airSlotWeights = baseList
                .Select(value => (int)Math.Floor(value))
                .Where(value => value > 0)
                .ToArray();
            var defaultEquipmentIds = defaultEquipList
                .Select(value => (int)Math.Floor(value))
                .ToArray();
            var defaultEquipmentTypes = defaultEquipmentIds
                .Select(id => equipmentTypes.GetValueOrDefault(id))
                .ToArray();
            var strengthenId = strengthenIds.GetValueOrDefault(currentId);
            var strengthen = strengthenStats.GetValueOrDefault(strengthenId);
            var oil = oilConsumption.GetValueOrDefault(currentId);
            stats[currentId] = new ShipStatBlock(
                Attr(0),
                Attr(1),
                Attr(2),
                Attr(3),
                Attr(4),
                Attr(ReloadAttrIndex),
                Growth(ReloadAttrIndex),
                Extra(ReloadAttrIndex),
                Attr(7),
                Attr(8),
                Attr(9),
                Attr(10),
                Attr(11),
                oil,
                armorType,
                strengthenId,
                StrengthenLimit(strengthen, ReloadStrengthIndex),
                StrengthenExpRatio(strengthen, ReloadStrengthIndex),
                blueprintStats.GetValueOrDefault(currentId / 10),
                transformReloadEffects,
                defaultEquipmentIds,
                defaultEquipmentTypes,
                airSlotWeights,
                ToIntArray(attrs),
                ToIntArray(growth),
                ToIntArray(growthExtra),
                strengthen);
            currentId = 0;
        }

        foreach (var line in File.ReadLines(path))
        {
            var entry = ShipStatsEntryRegex.Match(line);
            if (entry.Success)
            {
                StoreCurrent();
                currentId = int.Parse(entry.Groups[1].Value, CultureInfo.InvariantCulture);
                armorType = 0;
                attrs.Clear();
                growth.Clear();
                growthExtra.Clear();
                baseList.Clear();
                defaultEquipList.Clear();
                readingValues = null;
                continue;
            }

            if (currentId <= 0)
            {
                continue;
            }

            var trimmed = line.Trim();
            if (readingValues is null && trimmed.StartsWith("armor_type", StringComparison.Ordinal))
            {
                var match = LuaNumberRegex.Match(trimmed);
                armorType = match.Success
                    ? (int)Math.Floor(double.Parse(match.Value, CultureInfo.InvariantCulture))
                    : 0;
                continue;
            }

            if (readingValues is null && trimmed.StartsWith("attrs = {", StringComparison.Ordinal))
            {
                attrs.Clear();
                readingValues = attrs;
                continue;
            }

            if (readingValues is null && trimmed.StartsWith("attrs_growth = {", StringComparison.Ordinal))
            {
                growth.Clear();
                readingValues = growth;
                continue;
            }

            if (readingValues is null && trimmed.StartsWith("attrs_growth_extra = {", StringComparison.Ordinal))
            {
                growthExtra.Clear();
                readingValues = growthExtra;
                continue;
            }

            if (readingValues is null && trimmed.StartsWith("base_list = {", StringComparison.Ordinal))
            {
                baseList.Clear();
                readingValues = baseList;
                continue;
            }

            if (readingValues is null && trimmed.StartsWith("default_equip_list = {", StringComparison.Ordinal))
            {
                defaultEquipList.Clear();
                readingValues = defaultEquipList;
                continue;
            }

            if (readingValues is null)
            {
                continue;
            }

            if (trimmed.StartsWith("}", StringComparison.Ordinal))
            {
                readingValues = null;
                continue;
            }

            var number = LuaNumberRegex.Match(trimmed);
            if (number.Success)
            {
                readingValues.Add(double.Parse(number.Value, CultureInfo.InvariantCulture));
            }
        }

        StoreCurrent();

        lock (ShipStatsCacheLock)
        {
            ShipStatsCache[path] = new CachedShipStats(signature, stats);
        }

        return stats;
    }

    private static Dictionary<int, int> LoadStrengthenIds(string path)
    {
        var result = new Dictionary<int, int>();
        if (!File.Exists(path))
        {
            return result;
        }

        var currentId = 0;
        foreach (var line in File.ReadLines(path))
        {
            var entry = ShipTemplateEntryRegex.Match(line);
            if (entry.Success)
            {
                currentId = int.Parse(entry.Groups[1].Value, CultureInfo.InvariantCulture);
                continue;
            }

            if (currentId <= 0)
            {
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith("strengthen_id", StringComparison.Ordinal))
            {
                var match = LuaNumberRegex.Match(trimmed);
                if (match.Success)
                {
                    result[currentId] = int.Parse(match.Value, CultureInfo.InvariantCulture);
                }

                continue;
            }

            if (trimmed.StartsWith("}", StringComparison.Ordinal))
            {
                currentId = 0;
            }
        }

        return result;
    }

    private static Dictionary<int, int> LoadOilConsumption(string path)
    {
        var result = new Dictionary<int, int>();
        if (!File.Exists(path))
        {
            return result;
        }

        var currentId = 0;
        foreach (var line in File.ReadLines(path))
        {
            var entry = ShipTemplateEntryRegex.Match(line);
            if (entry.Success)
            {
                currentId = int.Parse(entry.Groups[1].Value, CultureInfo.InvariantCulture);
                continue;
            }

            if (currentId <= 0)
            {
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith("oil_at_end", StringComparison.Ordinal))
            {
                result[currentId] = LuaInt(trimmed);
                continue;
            }

            if (trimmed.StartsWith("}", StringComparison.Ordinal))
            {
                currentId = 0;
            }
        }

        return result;
    }

    private static Dictionary<int, int> LoadEquipmentTypes(string path)
    {
        var result = new Dictionary<int, int>();
        if (!File.Exists(path))
        {
            return result;
        }

        var currentId = 0;
        foreach (var line in File.ReadLines(path))
        {
            var entry = EquipmentStatisticsEntryRegex.Match(line);
            if (entry.Success)
            {
                currentId = int.Parse(entry.Groups[1].Value, CultureInfo.InvariantCulture);
                continue;
            }

            if (currentId <= 0)
            {
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith("type =", StringComparison.Ordinal))
            {
                result[currentId] = LuaInt(trimmed);
                currentId = 0;
                continue;
            }

            if (trimmed.StartsWith("}", StringComparison.Ordinal))
            {
                currentId = 0;
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<int>> LoadTransformReloadEffects(string path)
    {
        var result = new Dictionary<int, IReadOnlyList<int>>();
        if (!File.Exists(path))
        {
            return result;
        }

        var currentId = 0;
        var effects = new List<int>();
        var readingEffect = false;
        var effectLevel = 0;

        void StoreCurrent()
        {
            if (currentId > 0 && effects.Count > 0 && effects.Any(value => value != 0))
            {
                result[currentId] = effects.ToArray();
            }

            currentId = 0;
            effects.Clear();
            readingEffect = false;
            effectLevel = 0;
        }

        foreach (var line in File.ReadLines(path))
        {
            var entry = TransformDataTemplateEntryRegex.Match(line);
            if (entry.Success)
            {
                StoreCurrent();
                currentId = int.Parse(entry.Groups[1].Value, CultureInfo.InvariantCulture);
                continue;
            }

            if (currentId <= 0)
            {
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith("effect = {", StringComparison.Ordinal))
            {
                readingEffect = true;
                effectLevel = 0;
                continue;
            }

            if (!readingEffect)
            {
                continue;
            }

            if (trimmed.StartsWith("ship_id =", StringComparison.Ordinal) ||
                trimmed.StartsWith("edit_trans =", StringComparison.Ordinal))
            {
                readingEffect = false;
                effectLevel = 0;
                continue;
            }

            if (trimmed == "{")
            {
                effectLevel++;
                while (effects.Count < effectLevel)
                {
                    effects.Add(0);
                }

                continue;
            }

            if (effectLevel > 0 && trimmed.StartsWith("reload =", StringComparison.Ordinal))
            {
                effects[effectLevel - 1] += LuaInt(trimmed);
            }
        }

        StoreCurrent();
        return result;
    }

    private static Dictionary<int, ShipStrengthenStats> LoadStrengthenStats(string path)
    {
        var result = new Dictionary<int, ShipStrengthenStats>();
        if (!File.Exists(path))
        {
            return result;
        }

        var currentId = 0;
        List<int>? readingValues = null;
        var durability = new List<int>();
        var levelExp = new List<int>();

        void StoreCurrent()
        {
            if (currentId > 0 && durability.Count > 0 && levelExp.Count > 0)
            {
                result[currentId] = new ShipStrengthenStats(durability.ToArray(), levelExp.ToArray());
            }

            currentId = 0;
        }

        foreach (var line in File.ReadLines(path))
        {
            var entry = ShipStrengthenEntryRegex.Match(line);
            if (entry.Success)
            {
                StoreCurrent();
                currentId = int.Parse(entry.Groups[1].Value, CultureInfo.InvariantCulture);
                durability.Clear();
                levelExp.Clear();
                readingValues = null;
                continue;
            }

            if (currentId <= 0)
            {
                continue;
            }

            var trimmed = line.Trim();
            if (readingValues is null && trimmed.StartsWith("durability = {", StringComparison.Ordinal))
            {
                durability.Clear();
                readingValues = durability;
                continue;
            }

            if (readingValues is null && trimmed.StartsWith("level_exp = {", StringComparison.Ordinal))
            {
                levelExp.Clear();
                readingValues = levelExp;
                continue;
            }

            if (readingValues is null)
            {
                continue;
            }

            if (trimmed.StartsWith("}", StringComparison.Ordinal))
            {
                readingValues = null;
                continue;
            }

            var number = LuaNumberRegex.Match(trimmed);
            if (number.Success)
            {
                readingValues.Add(int.Parse(number.Value, CultureInfo.InvariantCulture));
            }
        }

        StoreCurrent();
        return result;
    }

    private static IReadOnlyDictionary<int, BlueprintStats> LoadBlueprintStats(
        string blueprintPath,
        string strengthenBlueprintPath)
    {
        var definitions = LoadBlueprintDefinitions(blueprintPath);
        var strengthenLevels = LoadBlueprintLevels(strengthenBlueprintPath);
        if (definitions.Count == 0 || strengthenLevels.Count == 0)
        {
            return new Dictionary<int, BlueprintStats>();
        }

        var result = new Dictionary<int, BlueprintStats>();
        foreach (var (blueprintId, definition) in definitions)
        {
            var levels = new List<BlueprintStatLevel>();
            foreach (var id in definition.StrengthenEffectIds)
            {
                if (strengthenLevels.TryGetValue(id, out var level))
                {
                    levels.Add(level);
                }
            }

            if (levels.Count == 0)
            {
                continue;
            }

            levels.Sort((left, right) => left.Level.CompareTo(right.Level));
            result[blueprintId] = new BlueprintStats(
                levels.Max(level => level.Level),
                definition.AttrExp,
                levels);
        }

        return result;
    }

    private static Dictionary<int, BlueprintDefinition> LoadBlueprintDefinitions(string path)
    {
        var result = new Dictionary<int, BlueprintDefinition>();
        if (!File.Exists(path))
        {
            return result;
        }

        var currentId = 0;
        List<int>? readingValues = null;
        var strengthenEffectIds = new List<int>();
        var attrExp = new List<int>();

        void StoreCurrent()
        {
            if (currentId > 0 && strengthenEffectIds.Count > 0 && attrExp.Count > 0)
            {
                result[currentId] = new BlueprintDefinition(
                    attrExp.ToArray(),
                    strengthenEffectIds.ToArray());
            }

            currentId = 0;
        }

        foreach (var line in File.ReadLines(path))
        {
            var entry = ShipBlueprintEntryRegex.Match(line);
            if (entry.Success)
            {
                StoreCurrent();
                currentId = int.Parse(entry.Groups[1].Value, CultureInfo.InvariantCulture);
                strengthenEffectIds.Clear();
                attrExp.Clear();
                readingValues = null;
                continue;
            }

            if (currentId <= 0)
            {
                continue;
            }

            var trimmed = line.Trim();
            if (readingValues is null && trimmed.StartsWith("strengthen_effect = {", StringComparison.Ordinal))
            {
                strengthenEffectIds.Clear();
                readingValues = strengthenEffectIds;
                continue;
            }

            if (readingValues is null && trimmed.StartsWith("attr_exp = {", StringComparison.Ordinal))
            {
                attrExp.Clear();
                readingValues = attrExp;
                continue;
            }

            if (readingValues is null)
            {
                continue;
            }

            if (trimmed.StartsWith("}", StringComparison.Ordinal))
            {
                readingValues = null;
                continue;
            }

            var number = LuaNumberRegex.Match(trimmed);
            if (number.Success)
            {
                readingValues.Add(int.Parse(number.Value, CultureInfo.InvariantCulture));
            }
        }

        StoreCurrent();
        return result;
    }

    private static Dictionary<int, BlueprintStatLevel> LoadBlueprintLevels(string path)
    {
        var result = new Dictionary<int, BlueprintStatLevel>();
        if (!File.Exists(path))
        {
            return result;
        }

        var currentId = 0;
        var level = 0;
        var needExp = 0;
        List<int>? readingValues = null;
        var effect = new List<int>();

        void StoreCurrent()
        {
            if (currentId > 0 && level > 0 && effect.Count > 0)
            {
                result[currentId] = new BlueprintStatLevel(level, needExp, effect.ToArray());
            }

            currentId = 0;
        }

        foreach (var line in File.ReadLines(path))
        {
            var entry = ShipStrengthenBlueprintEntryRegex.Match(line);
            if (entry.Success)
            {
                StoreCurrent();
                currentId = int.Parse(entry.Groups[1].Value, CultureInfo.InvariantCulture);
                level = 0;
                needExp = 0;
                effect.Clear();
                readingValues = null;
                continue;
            }

            if (currentId <= 0)
            {
                continue;
            }

            var trimmed = line.Trim();
            if (readingValues is null && trimmed.StartsWith("lv", StringComparison.Ordinal))
            {
                level = LuaInt(trimmed);
                continue;
            }

            if (readingValues is null && trimmed.StartsWith("need_exp", StringComparison.Ordinal))
            {
                needExp = LuaInt(trimmed);
                continue;
            }

            if (readingValues is null && trimmed.StartsWith("effect = {", StringComparison.Ordinal))
            {
                effect.Clear();
                readingValues = effect;
                continue;
            }

            if (readingValues is null)
            {
                continue;
            }

            if (trimmed.StartsWith("}", StringComparison.Ordinal))
            {
                readingValues = null;
                continue;
            }

            var number = LuaNumberRegex.Match(trimmed);
            if (number.Success)
            {
                readingValues.Add(int.Parse(number.Value, CultureInfo.InvariantCulture));
            }
        }

        StoreCurrent();
        return result;
    }

    private static int LuaInt(string line)
    {
        var match = LuaNumberRegex.Match(line);
        return match.Success ? int.Parse(match.Value, CultureInfo.InvariantCulture) : 0;
    }

    private static int[] ToIntArray(IEnumerable<double> values) =>
        values.Select(value => (int)Math.Floor(value)).ToArray();

    private static int StatValue(IReadOnlyList<int> values, int index) =>
        index >= 0 && index < values.Count ? values[index] : 0;

    private static int StrengthenLimit(ShipStrengthenStats? stats, int index) =>
        stats is null ? 0 : StatValue(stats.Limits, index);

    private static int StrengthenExpRatio(ShipStrengthenStats? stats, int index) =>
        stats is null ? 0 : StatValue(stats.ExpRatios, index);

    private static int EquipmentStatBonus(IEnumerable<EquipmentItemState> items, string attributeType) =>
        items.SelectMany(item => item.Attributes)
            .Where(attribute => string.Equals(attribute.Type, attributeType, StringComparison.OrdinalIgnoreCase))
            .Sum(attribute => attribute.Value);

    private static int StrengthIndexForAttribute(int attributeIndex) => attributeIndex switch
    {
        CannonAttrIndex => CannonStrengthIndex,
        TorpedoAttrIndex => TorpedoStrengthIndex,
        AntiAircraftAttrIndex => AntiAircraftStrengthIndex,
        AirAttrIndex => AirStrengthIndex,
        ReloadAttrIndex => ReloadStrengthIndex,
        _ => -1
    };

    private static int StrengthAttributeId(int strengthIndex) => strengthIndex switch
    {
        CannonStrengthIndex => CannonStrengthAttrId,
        TorpedoStrengthIndex => TorpedoStrengthAttrId,
        AntiAircraftStrengthIndex => AntiAircraftStrengthAttrId,
        AirStrengthIndex => AirStrengthAttrId,
        ReloadStrengthIndex => ReloadStrengthAttrId,
        _ => 0
    };

    private static bool IsIntimacyScaledAttribute(int attributeIndex) =>
        attributeIndex is not SpeedAttrIndex and not LuckAttrIndex;

    private static ShipEffectiveStats? EffectiveStats(
        ShipStatBlock? stats,
        int level,
        int intimacy,
        ShipStrengthState strength,
        IReadOnlyDictionary<int, int>? transformLevels = null,
        IEnumerable<EquipmentItemState>? equipment = null)
    {
        if (stats is null)
        {
            return null;
        }

        var equipmentItems = equipment?.Where(item => !item.Empty).ToArray() ?? [];
        var reload = EffectiveAttribute(stats, ReloadAttrIndex, level, intimacy, strength, transformLevels);
        reload += EquipmentStatBonus(equipmentItems, "reload");

        return new ShipEffectiveStats(
            EffectiveAttribute(stats, DurabilityAttrIndex, level, intimacy, strength) +
            EquipmentStatBonus(equipmentItems, "durability"),
            EffectiveAttribute(stats, CannonAttrIndex, level, intimacy, strength) +
            EquipmentStatBonus(equipmentItems, "cannon"),
            EffectiveAttribute(stats, TorpedoAttrIndex, level, intimacy, strength) +
            EquipmentStatBonus(equipmentItems, "torpedo"),
            EffectiveAttribute(stats, AntiAircraftAttrIndex, level, intimacy, strength) +
            EquipmentStatBonus(equipmentItems, "antiaircraft"),
            EffectiveAttribute(stats, AirAttrIndex, level, intimacy, strength) +
            EquipmentStatBonus(equipmentItems, "air"),
            reload,
            EffectiveAttribute(stats, HitAttrIndex, level, intimacy, strength) +
            EquipmentStatBonus(equipmentItems, "hit"),
            EffectiveAttribute(stats, DodgeAttrIndex, level, intimacy, strength) +
            EquipmentStatBonus(equipmentItems, "dodge"),
            EffectiveAttribute(stats, SpeedAttrIndex, level, intimacy, strength) +
            EquipmentStatBonus(equipmentItems, "speed"),
            EffectiveAttribute(stats, LuckAttrIndex, level, intimacy, strength) +
            EquipmentStatBonus(equipmentItems, "luck"),
            EffectiveAttribute(stats, AntiSubAttrIndex, level, intimacy, strength) +
            EquipmentStatBonus(equipmentItems, "antisub"),
            stats.OilConsumption,
            stats.ArmorType);
    }

    private static int EffectiveReload(
        ShipStatBlock? stats,
        int level,
        int intimacy,
        ShipStrengthState strength,
        IReadOnlyDictionary<int, int>? transformLevels = null)
    {
        return EffectiveStats(stats, level, intimacy, strength, transformLevels)?.Reload ?? 0;
    }

    private static int EffectiveAttribute(
        ShipStatBlock stats,
        int attributeIndex,
        int level,
        int intimacy,
        ShipStrengthState strength,
        IReadOnlyDictionary<int, int>? transformLevels = null)
    {
        var safeLevel = Math.Max(1, level);
        var value = StatValue(stats.BaseAttributes, attributeIndex) +
                    (safeLevel - 1) * StatValue(stats.AttributeGrowth, attributeIndex) / 1000.0;
        if (safeLevel > ExtraAttrLevelLimit)
        {
            value += (safeLevel - ExtraAttrLevelLimit) *
                     StatValue(stats.AttributeGrowthExtra, attributeIndex) / 1000.0;
        }

        var strengthIndex = StrengthIndexForAttribute(attributeIndex);
        if (strengthIndex >= 0)
        {
            value += stats.BlueprintStats is not null
                ? BlueprintStatAddition(stats.BlueprintStats, strength.BlueprintLevel, strength.BlueprintExp,
                    strengthIndex)
                : StrengthenAddition(stats.StrengthenStats, strengthIndex,
                    strength.StrengthExp(StrengthAttributeId(strengthIndex)));
        }

        if (IsIntimacyScaledAttribute(attributeIndex))
        {
            value *= IntimacyMultiplier(intimacy);
        }

        var effective = (int)Math.Floor(value + 1e-9);
        if (attributeIndex == ReloadAttrIndex)
        {
            effective += TransformReloadAddition(stats, transformLevels);
        }

        return effective;
    }

    private static void ApplyFleetTechReloadAdditions(List<ShipState> ships, JsonElement fleetTechMap)
    {
        if (fleetTechMap.ValueKind != JsonValueKind.Object || ships.Count == 0)
        {
            return;
        }

        var maxLevelByGroup = ships
            .Where(ship => !ship.IsBulin && ship.TemplateId > 0)
            .GroupBy(ship => FirstPositive(ship.GroupId, ship.TemplateId / 10))
            .ToDictionary(group => group.Key, group => group.Max(ship => ship.Level));
        if (maxLevelByGroup.Count == 0)
        {
            return;
        }

        var reloadByShipType = new Dictionary<int, int>();
        foreach (var (groupId, maxLevel) in maxLevelByGroup)
        {
            var tech = MapObject(fleetTechMap, groupId);
            if (tech.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            AddFleetTechReloadBonus(reloadByShipType, tech, "get");
            if (maxLevel >= FleetTechLevelThreshold)
            {
                AddFleetTechReloadBonus(reloadByShipType, tech, "level");
            }
        }

        foreach (var ship in ships)
        {
            if (ship.Reload <= 0 ||
                ship.ShipTypeId <= 0 ||
                !reloadByShipType.TryGetValue(ship.ShipTypeId, out var addition) ||
                addition <= 0)
            {
                continue;
            }

            ship.Reload += addition;
            ship.StatsDetail = ReplaceStatLine(ship.StatsDetail, "장전", ship.Reload);
            ship.StatRows = ReplaceStatRow(ship.StatRows, "장전", ship.Reload);
        }
    }

    private static void AddFleetTechReloadBonus(
        Dictionary<int, int> reloadByShipType,
        JsonElement tech,
        string prefix)
    {
        if (JsonInt(tech, $"{prefix}_attr") != ReloadAttributeTypeId)
        {
            return;
        }

        var value = JsonInt(tech, $"{prefix}_value");
        if (value <= 0)
        {
            return;
        }

        foreach (var shipType in Array(tech, $"{prefix}_shiptypes").Select(IntValue).Where(value => value > 0))
        {
            reloadByShipType[shipType] = reloadByShipType.GetValueOrDefault(shipType) + value;
        }
    }

    private static string ReplaceStatLine(string detail, string label, int value)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return detail;
        }

        var prefix = $"{label} ";
        var lines = detail.Split(Environment.NewLine);
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith(prefix, StringComparison.Ordinal))
            {
                lines[i] = StatPart(label, value, includeZero: true);
                break;
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static IReadOnlyList<ShipStatRow> ReplaceStatRow(
        IReadOnlyList<ShipStatRow> rows,
        string label,
        int value)
    {
        if (rows.Count == 0)
        {
            return rows;
        }

        return rows
            .Select(row => string.Equals(row.Label, label, StringComparison.Ordinal)
                ? row with { ValueText = StatText(value) }
                : row)
            .ToArray();
    }

    private static ShipStrengthState ReadShipStrengthState(JsonElement raw)
    {
        var attributeExp = new Dictionary<int, int>();
        var blueprintLevel = 0;
        var blueprintExp = 0;
        var first = true;
        foreach (var item in Array(raw, "strength_list"))
        {
            var id = JsonInt(item, "id");
            if (id > 0)
            {
                attributeExp[id] = JsonInt(item, "exp");
            }

            if (first)
            {
                blueprintLevel = id;
                blueprintExp = JsonInt(item, "exp");
                first = false;
            }
        }

        return new ShipStrengthState(attributeExp, blueprintLevel, blueprintExp);
    }

    private static (int Value, string Text) ShipEnhancement(
        ShipStatBlock? stats,
        int level,
        ShipStrengthState strength,
        bool hasFullState)
    {
        if (stats?.BlueprintStats is not null)
        {
            var blueprintLevel = Math.Max(0, strength.BlueprintLevel);
            return (blueprintLevel, hasFullState ? $"개발 Lv.{blueprintLevel:N0}" : "-");
        }

        if (stats?.StrengthenStats is null)
        {
            return (0, hasFullState ? "0%" : "-");
        }

        long currentExp = 0;
        long maximumExp = 0;
        var levelFactor = 3d + 7d * Math.Min(Math.Max(level, 1), 100) / 100d;
        for (var strengthIndex = 0; strengthIndex <= ReloadStrengthIndex; strengthIndex++)
        {
            var durability = Math.Max(0, StrengthenLimit(stats.StrengthenStats, strengthIndex));
            var ratio = Math.Max(1, StrengthenExpRatio(stats.StrengthenStats, strengthIndex));
            var pointLimit = (int)Math.Floor(levelFactor * durability * 0.1d + 1e-9);
            var expLimit = (long)pointLimit * ratio;
            if (expLimit <= 0)
            {
                continue;
            }

            maximumExp += expLimit;
            currentExp += Math.Min(Math.Max(0, strength.StrengthExp(StrengthAttributeId(strengthIndex))), expLimit);
        }

        if (maximumExp <= 0)
        {
            return (0, hasFullState ? "0%" : "-");
        }

        var percentage = (int)Math.Round(currentExp * 100d / maximumExp, MidpointRounding.AwayFromZero);
        return (Math.Clamp(percentage, 0, 100), $"{Math.Clamp(percentage, 0, 100):N0}%");
    }

    private static IReadOnlyDictionary<int, int> ReadTransformLevels(JsonElement raw)
    {
        var result = new Dictionary<int, int>();
        foreach (var item in Array(raw, "transform_list"))
        {
            var id = FirstPositive(JsonInt(item, "id"), IntValue(item));
            if (id <= 0)
            {
                continue;
            }

            result[id] = FirstPositive(JsonInt(item, "level"), 1);
        }

        return result;
    }

    private static int TransformReloadAddition(
        ShipStatBlock stats,
        IReadOnlyDictionary<int, int>? transformLevels)
    {
        if (transformLevels is null || transformLevels.Count == 0 || stats.TransformReloadEffects.Count == 0)
        {
            return 0;
        }

        var total = 0;
        foreach (var (id, level) in transformLevels)
        {
            if (!stats.TransformReloadEffects.TryGetValue(id, out var effects))
            {
                continue;
            }

            var cappedLevel = Math.Clamp(level, 0, effects.Count);
            for (var index = 0; index < cappedLevel; index++)
            {
                total += effects[index];
            }
        }

        return total;
    }

    private static int StrengthenAddition(ShipStrengthenStats? stats, int strengthIndex, int strengthExp)
    {
        var ratio = Math.Max(StrengthenExpRatio(stats, strengthIndex), 1);
        var cap = Math.Max(0, StrengthenLimit(stats, strengthIndex)) * ratio;
        var exp = cap > 0 ? Math.Min(strengthExp, cap) : strengthExp;
        return exp > 0 ? (int)Math.Floor(exp / (double)ratio + 1e-9) : 0;
    }

    private static int BlueprintStatAddition(BlueprintStats stats, int level, int exp, int strengthIndex)
    {
        if (level <= 0)
        {
            return 0;
        }

        var cappedLevel = Math.Clamp(level, 0, stats.MaxLevel);
        var effect = stats.Levels
            .Where(item => item.Level <= cappedLevel)
            .Sum(item => (double)StatValue(item.Effects, strengthIndex));
        if (cappedLevel < stats.MaxLevel && exp > 0)
        {
            var next = stats.Levels.FirstOrDefault(item => item.Level == cappedLevel + 1);
            if (next is not null && next.NeedExp > 0)
            {
                effect += exp / (double)next.NeedExp * StatValue(next.Effects, strengthIndex);
            }
        }

        return effect > 0
            ? (int)Math.Floor(effect / (double)Math.Max(StatValue(stats.AttrExp, strengthIndex), 1) + 1e-9)
            : 0;
    }

    private static double IntimacyMultiplier(int intimacy)
    {
        return intimacy switch
        {
            >= 20000 => 1.12,
            >= 10001 => 1.09,
            >= 10000 => 1.06,
            >= 8100 => 1.03,
            >= 6100 => 1.01,
            _ => 1.0
        };
    }

    private static string FileSignature(string path)
    {
        if (!File.Exists(path))
        {
            return "missing";
        }

        var info = new FileInfo(path);
        return $"{info.LastWriteTimeUtc.Ticks}:{info.Length}";
    }

    private static string ShipStatsSummary(ShipEffectiveStats? stats)
    {
        if (stats is null)
        {
            return "-";
        }

        return string.Join(" · ", new[]
        {
            StatPart("내구", stats.Durability, includeZero: true),
            StatPart("포격", stats.Cannon),
            StatPart("뇌장", stats.Torpedo),
            StatPart("항공", stats.Air),
            StatPart("대공", stats.AntiAircraft)
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string ShipStatsDetail(ShipEffectiveStats? stats)
    {
        if (stats is null)
        {
            return "스탯 정보 없음";
        }

        return string.Join(Environment.NewLine, ShipStatRows(stats)
            .Select(row => $"{row.Label} {row.ValueText}"));
    }

    private static IReadOnlyList<ShipStatRow> ShipStatRows(ShipEffectiveStats? stats)
    {
        if (stats is null)
        {
            return [];
        }

        return
        [
            new ShipStatRow("내구", StatText(stats.Durability), 10),
            new ShipStatRow("포격", StatText(stats.Cannon), 20),
            new ShipStatRow("뇌장", StatText(stats.Torpedo), 30),
            new ShipStatRow("대공", StatText(stats.AntiAircraft), 40),
            new ShipStatRow("대잠", StatText(stats.AntiSub), 50),
            new ShipStatRow("항공", StatText(stats.Air), 60),
            new ShipStatRow("장전", StatText(stats.Reload), 70),
            new ShipStatRow("기동", StatText(stats.Dodge), 80),
            new ShipStatRow("명중", StatText(stats.Hit), 90),
            new ShipStatRow("항속", StatText(stats.Speed), 100),
            new ShipStatRow("행운", StatText(stats.Luck), 110),
            new ShipStatRow("장갑", FirstNonEmpty(ArmorTypeLabel(stats.ArmorType), "-"), 120),
            new ShipStatRow("소모", stats.OilConsumption > 0 ? stats.OilConsumption.ToString("N0", CultureInfo.InvariantCulture) : "-", 130)
        ];
    }

    private static string StatPart(string label, int value, bool includeZero = false) =>
        includeZero || value > 0 ? $"{label} {value:N0}" : "";

    private static string StatText(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string ArmorTypeLabel(int armorType) => armorType switch
    {
        1 => "경장갑",
        2 => "중형장갑",
        3 => "중장갑",
        _ => ""
    };
}
