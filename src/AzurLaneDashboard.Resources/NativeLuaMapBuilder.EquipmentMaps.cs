using System.Globalization;
using System.Text.RegularExpressions;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Resources;

internal sealed partial class NativeLuaMapBuilder
{
    private readonly record struct FireRateWeapon(int Id, IReadOnlyDictionary<string, object?> Data);

    private static Dictionary<string, Dictionary<string, object?>> BuildEquipmentMap(
        IReadOnlyList<Dictionary<string, object?>> equipmentTemplates,
        IReadOnlyList<Dictionary<string, object?>> equipmentStats)
    {
        var statsById = equipmentStats
            .Where(item => SafeInt(Get(item, "id")) != 0)
            .ToDictionary(item => SafeInt(Get(item, "id")), item => item);
        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var template in equipmentTemplates)
        {
            var equipmentId = SafeInt(Get(template, "id"));
            if (equipmentId == 0)
            {
                continue;
            }

            var baseId = SafeInt(Get(template, "base"), equipmentId);
            statsById.TryGetValue(equipmentId, out var stat);
            stat ??= new Dictionary<string, object?>();
            var statBaseId = SafeInt(Get(stat, "base"), baseId);
            statsById.TryGetValue(statBaseId, out var baseStat);
            if (baseStat is null)
            {
                statsById.TryGetValue(baseId, out baseStat);
            }

            baseStat ??= new Dictionary<string, object?>();
            object? Inherited(string key, object? defaultValue = null)
            {
                var value = Get(stat, key);
                if (HasValue(value))
                {
                    return value;
                }

                var baseValue = Get(baseStat, key);
                if (HasValue(baseValue))
                {
                    return baseValue;
                }

                return defaultValue ?? "";
            }

            var level = SafeInt(Get(template, "level"), 1);
            var iconId = FirstPositive(SafeInt(Inherited("icon")), statBaseId, baseId);
            var attributes = EquipmentAttributes(equipmentId, baseId, statsById);
            var reloadBonus = EquipmentAttributeValue(attributes, "reload");
            result[equipmentId.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["id"] = equipmentId,
                ["template_id"] = equipmentId,
                ["base"] = baseId,
                ["name"] = FirstNonEmpty(Inherited("name"), $"equipment:{equipmentId}") ?? $"equipment:{equipmentId}",
                ["rarity"] = Inherited("rarity"),
                ["level"] = level,
                ["enhance_level"] = Math.Max(0, level - 1),
                ["type"] = FirstNonEmpty(Inherited("type"), Get(template, "type")) ?? "",
                ["tech"] = Inherited("tech"),
                ["group"] = Get(template, "group") ?? "",
                ["next"] = Get(template, "next") ?? "",
                ["prev"] = Get(template, "prev") ?? "",
                ["attribute_1"] = EquipmentAttributeType(attributes, 1),
                ["attribute_2"] = EquipmentAttributeType(attributes, 2),
                ["attribute_3"] = EquipmentAttributeType(attributes, 3),
                ["value_1"] = EquipmentAttributeValue(attributes, 1),
                ["value_2"] = EquipmentAttributeValue(attributes, 2),
                ["value_3"] = EquipmentAttributeValue(attributes, 3),
                ["attributes"] = attributes,
                ["reload_bonus"] = reloadBonus,
                ["icon_id"] = iconId,
                ["icon_url"] = ""
            };
        }

        return result;
    }

    private static IReadOnlyList<Dictionary<string, object?>> EquipmentAttributes(
        int equipmentId,
        int fallbackBaseId,
        IReadOnlyDictionary<int, Dictionary<string, object?>> statsById)
    {
        var attributes = new List<Dictionary<string, object?>>();
        for (var index = 1; index <= 3; index++)
        {
            var type = Convert.ToString(
                ResolveEquipmentStatField(equipmentId, fallbackBaseId, statsById, $"attribute_{index}"),
                CultureInfo.InvariantCulture) ?? "";
            var value = SafeInt(ResolveEquipmentStatField(equipmentId, fallbackBaseId, statsById, $"value_{index}"));
            if (string.IsNullOrWhiteSpace(type) || value == 0)
            {
                continue;
            }

            attributes.Add(new Dictionary<string, object?>
            {
                ["index"] = index,
                ["type"] = type,
                ["label"] = EquipmentAttributeRow.LabelFor(type),
                ["value"] = value
            });
        }

        return attributes;
    }

    private static object? ResolveEquipmentStatField(
        int equipmentId,
        int fallbackBaseId,
        IReadOnlyDictionary<int, Dictionary<string, object?>> statsById,
        string key)
    {
        return ResolveEquipmentStatField(equipmentId, fallbackBaseId, statsById, key, new HashSet<int>());
    }

    private static object? ResolveEquipmentStatField(
        int equipmentId,
        int fallbackBaseId,
        IReadOnlyDictionary<int, Dictionary<string, object?>> statsById,
        string key,
        HashSet<int> visiting)
    {
        if (equipmentId <= 0 || !visiting.Add(equipmentId))
        {
            return null;
        }

        if (statsById.TryGetValue(equipmentId, out var stat))
        {
            var value = Get(stat, key);
            if (HasValue(value))
            {
                return value;
            }

            var baseId = SafeInt(Get(stat, "base"));
            if (baseId > 0)
            {
                return ResolveEquipmentStatField(baseId, 0, statsById, key, visiting);
            }
        }

        if (fallbackBaseId > 0 && fallbackBaseId != equipmentId)
        {
            return ResolveEquipmentStatField(fallbackBaseId, 0, statsById, key, visiting);
        }

        return null;
    }

    private static string EquipmentAttributeType(IEnumerable<Dictionary<string, object?>> attributes, int index) =>
        Convert.ToString(attributes.FirstOrDefault(attribute => SafeInt(Get(attribute, "index")) == index)?["type"],
            CultureInfo.InvariantCulture) ?? "";

    private static int EquipmentAttributeValue(IEnumerable<Dictionary<string, object?>> attributes, int index) =>
        SafeInt(attributes.FirstOrDefault(attribute => SafeInt(Get(attribute, "index")) == index)?["value"]);

    private static int EquipmentAttributeValue(
        IEnumerable<Dictionary<string, object?>> attributes,
        string attributeName) =>
        attributes
            .Where(attribute => string.Equals(
                Convert.ToString(Get(attribute, "type"), CultureInfo.InvariantCulture),
                attributeName,
                StringComparison.OrdinalIgnoreCase))
            .Sum(attribute => SafeInt(Get(attribute, "value")));

    private static Dictionary<string, Dictionary<string, object?>> BuildFireRateEquipmentMap(
        IReadOnlyList<Dictionary<string, object?>> equipmentStats,
        IReadOnlyList<Dictionary<string, object?>> weaponProperties)
    {
        var weaponsById = BuildWeaponLookup(weaponProperties);
        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);

        foreach (var equipment in equipmentStats)
        {
            var equipmentId = SafeInt(Get(equipment, "id"));
            if (equipmentId == 0)
            {
                continue;
            }

            var weaponIds = EquipmentWeaponIds(equipment);
            if (weaponIds.Length == 0)
            {
                continue;
            }

            var weapons = ResolveEquipmentWeapons(weaponIds, weaponsById);
            if (weapons.Length == 0)
            {
                continue;
            }

            var reloads = WeaponReloads(weapons);
            if (reloads.Length == 0)
            {
                continue;
            }

            result[equipmentId.ToString(CultureInfo.InvariantCulture)] =
                BuildFireRateEquipmentRow(equipment, equipmentId, weaponIds, weapons, reloads);
        }

        return result;
    }

    private static Dictionary<int, Dictionary<string, object?>> BuildWeaponLookup(
        IReadOnlyList<Dictionary<string, object?>> weaponProperties)
    {
        var raw = weaponProperties
            .Where(item => SafeInt(Get(item, "id")) != 0)
            .GroupBy(item => SafeInt(Get(item, "id")))
            .ToDictionary(group => group.Key, group => group.Last());
        var resolved = new Dictionary<int, Dictionary<string, object?>>();

        Dictionary<string, object?> Resolve(int id, HashSet<int> stack)
        {
            if (resolved.TryGetValue(id, out var cached))
            {
                return cached;
            }

            if (!raw.TryGetValue(id, out var current))
            {
                return new Dictionary<string, object?>();
            }

            var baseId = SafeInt(Get(current, "base"));
            var merged = baseId > 0 && stack.Add(id)
                ? new Dictionary<string, object?>(Resolve(baseId, stack), StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (key, value) in current)
            {
                if (HasValue(value))
                {
                    merged[key] = value;
                }
            }

            merged["id"] = id;
            resolved[id] = merged;
            return merged;
        }

        foreach (var id in raw.Keys)
        {
            Resolve(id, new HashSet<int>());
        }

        return resolved;
    }

    private static int[] EquipmentWeaponIds(IReadOnlyDictionary<string, object?> equipment)
    {
        return IntArray(Get(equipment, "weapon_id"))
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
    }

    private static FireRateWeapon[] ResolveEquipmentWeapons(
        IEnumerable<int> weaponIds,
        IReadOnlyDictionary<int, Dictionary<string, object?>> weaponsById)
    {
        return weaponIds
            .Where(weaponsById.ContainsKey)
            .Select(id => new FireRateWeapon(id, weaponsById[id]))
            .ToArray();
    }

    private static int[] WeaponReloads(IEnumerable<FireRateWeapon> weapons)
    {
        return weapons
            .Select(item => SafeInt(Get(item.Data, "reload_max")))
            .Where(value => value > 0)
            .ToArray();
    }

    private static Dictionary<string, object?> BuildFireRateEquipmentRow(
        IReadOnlyDictionary<string, object?> equipment,
        int equipmentId,
        int[] weaponIds,
        FireRateWeapon[] weapons,
        int[] reloads)
    {
        var primary = weapons[0];
        var primaryReload = SafeInt(Get(primary.Data, "reload_max"));
        var weaponDamage = weapons.Sum(item => SafeInt(Get(item.Data, "damage")));
        var damageText = Convert.ToString(Get(equipment, "damage"), CultureInfo.InvariantCulture) ?? "";
        var damagePerCycle = FirstPositive(ParseDamageText(damageText), weaponDamage);

        return new Dictionary<string, object?>
        {
            ["id"] = equipmentId,
            ["name"] = FirstNonEmpty(Get(equipment, "name"), $"equipment:{equipmentId}") ?? $"equipment:{equipmentId}",
            ["type"] = SafeInt(Get(equipment, "type")),
            ["rarity"] = SafeInt(Get(equipment, "rarity")),
            ["weapon_ids"] = weaponIds,
            ["weapon_count"] = weapons.Length,
            ["primary_weapon_id"] = primary.Id,
            ["static_reload_max"] = primaryReload,
            ["reload_max"] = (int)Math.Round(reloads.Average(), MidpointRounding.AwayFromZero),
            ["min_reload_max"] = reloads.Min(),
            ["max_reload_max"] = reloads.Max(),
            ["damage_text"] = damageText,
            ["damage_per_cycle"] = damagePerCycle,
            ["weapon_damage"] = weaponDamage,
            ["initial_over_heat"] = weapons.Max(item => SafeInt(Get(item.Data, "initial_over_heat"))),
            ["recover_time"] = weapons.Max(item => SafeInt(Get(item.Data, "recover_time"))),
            ["queue"] = weapons.Max(item => SafeInt(Get(item.Data, "queue"))),
            ["weapon_type"] = SafeInt(Get(primary.Data, "type")),
            ["range"] = SafeInt(Get(primary.Data, "range")),
            ["corrected"] = SafeInt(Get(primary.Data, "corrected")),
            ["attack_attribute"] = SafeInt(Get(primary.Data, "attack_attribute")),
            ["attack_attribute_ratio"] = SafeInt(Get(primary.Data, "attack_attribute_ratio")),
            ["bullet_ids"] = weapons.SelectMany(item => IntArray(Get(item.Data, "bullet_ID"))).Distinct().ToArray(),
            ["barrage_ids"] = weapons.SelectMany(item => IntArray(Get(item.Data, "barrage_ID"))).Distinct().ToArray()
        };
    }

    private static int ParseDamageText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var numbers = Regex.Matches(value, @"\d+", RegexOptions.CultureInvariant)
            .Select(match => SafeInt(match.Value))
            .Where(number => number > 0)
            .Take(2)
            .ToArray();
        return numbers.Length switch
        {
            0 => 0,
            1 => numbers[0],
            _ => numbers[0] * numbers[1]
        };
    }

    private static Dictionary<string, Dictionary<string, object?>> BuildEquipmentSkinMap(
        IReadOnlyList<Dictionary<string, object?>> equipmentSkins)
    {
        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var skin in equipmentSkins)
        {
            var skinId = SafeInt(Get(skin, "id"));
            if (skinId == 0)
            {
                continue;
            }

            var iconId = FirstPositive(SafeInt(Get(skin, "icon")), skinId);
            result[skinId.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["id"] = skinId,
                ["name"] = FirstNonEmpty(Get(skin, "name"), $"equipment skin:{skinId}") ?? $"equipment skin:{skinId}",
                ["desc"] = Get(skin, "desc") ?? "",
                ["rarity"] = Get(skin, "rarity") ?? "",
                ["type"] = Get(skin, "type") ?? "",
                ["equip_type"] = Get(skin, "equip_type") ?? Array.Empty<int>(),
                ["themeid"] = Get(skin, "themeid") ?? "",
                ["icon_id"] = iconId,
                ["icon_url"] = ""
            };
        }

        return result;
    }

    private static Dictionary<string, Dictionary<string, object?>> BuildSpWeaponMap(
        IReadOnlyList<Dictionary<string, object?>> spWeaponStats)
    {
        var statsById = spWeaponStats
            .Where(item => SafeInt(Get(item, "id")) != 0)
            .ToDictionary(item => SafeInt(Get(item, "id")), item => item);
        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var item in spWeaponStats)
        {
            var spWeaponId = SafeInt(Get(item, "id"));
            if (spWeaponId == 0)
            {
                continue;
            }

            object? Inherited(string key, object? defaultValue = null)
            {
                var value = Get(item, key);
                if (HasValue(value))
                {
                    return value;
                }

                var baseId = SafeInt(Get(item, "base"));
                if (baseId != 0 && statsById.TryGetValue(baseId, out var baseItem))
                {
                    var baseValue = Get(baseItem, key);
                    if (HasValue(baseValue))
                    {
                        return baseValue;
                    }
                }

                return defaultValue ?? "";
            }

            var level = SafeInt(Get(item, "level"), 1);
            result[spWeaponId.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>
            {
                ["id"] = spWeaponId,
                ["name"] = Inherited("name", $"spweapon:{spWeaponId}"),
                ["rarity"] = Inherited("rarity"),
                ["level"] = level,
                ["enhance_level"] = Math.Max(0, level - 1),
                ["type"] = Inherited("type"),
                ["unique"] = Inherited("unique"),
                ["attribute_1"] = Inherited("attribute_1"),
                ["attribute_2"] = Inherited("attribute_2"),
                ["value_1"] = Get(item, "value_1") ?? "",
                ["value_2"] = Get(item, "value_2") ?? "",
                ["effect_id"] = Get(item, "effect_id") ?? "",
                ["icon_id"] = FirstPositive(SafeInt(Inherited("icon")), spWeaponId),
                ["icon_url"] = ""
            };
        }

        return result;
    }
}
