using System.Globalization;
using System.Text.Json;
using AzurLaneDashboard.Core.Models;

namespace AzurLaneDashboard.Data.Services;

public sealed partial class NativeSnapshotService
{
    private const double ReloadK1 = 6.0;
    private const double ReloadK2 = 100.0;
    private const double ReloadK3 = 3.14;
    private const double AirAssistReloadRatio = 2.2;

    private sealed record FireRateEquipmentSlot(
        EquipmentItemState Item,
        JsonElement Info,
        int ReloadMax,
        int StaticReloadMax);

    private sealed record FireRateSkillAdjustment(
        double LoadSpeedMultiplier,
        double WeaponReloadRequirementMultiplier,
        string Description)
    {
        public static FireRateSkillAdjustment None { get; } = new(1.0, 1.0, "");
    }

    private static List<FleetDamageRow> BuildFleetDamageRows(
        IEnumerable<ShipState> ships,
        JsonElement fireRateEquipmentMap,
        JsonElement equipmentMap,
        JsonElement skillMap,
        FleetLayout fleetLayout)
    {
        if (fireRateEquipmentMap.ValueKind != JsonValueKind.Object || !fleetLayout.HasFleetData)
        {
            return [];
        }

        var rows = new List<FleetDamageRow>();
        foreach (var ship in ships.Where(ship => !ship.IsBulin))
        {
            if (!fleetLayout.Assignments.TryGetValue(ship.Uid, out var fleet))
            {
                continue;
            }

            if (!IsRegularFleet(fleet.FleetId))
            {
                continue;
            }

            var shipTypeGroup = ShipTypeGroup(ship.ShipTypeId);
            var detailRows = new List<FleetDamageRow>();
            foreach (var timedSlot in TimedEquipmentSlots(ship, fireRateEquipmentMap))
            {
                detailRows.Add(BuildWeaponTimingRow(
                    ship,
                    fleet,
                    timedSlot,
                    shipTypeGroup,
                    AttackRole(timedSlot.Item),
                    skillMap));
            }

            var airstrikeSlots = shipTypeGroup == "main"
                ? AirstrikeItems(ship, equipmentMap)
                    .Select(item => TryReadFireRateEquipment(fireRateEquipmentMap, item, out var slot) ? slot : null)
                    .Where(slot => slot is not null)
                    .Cast<FireRateEquipmentSlot>()
                    .ToArray()
                : [];
            if (airstrikeSlots.Length > 0)
            {
                detailRows.Add(BuildAirstrikeTimingRow(ship, fleet, shipTypeGroup, airstrikeSlots, skillMap));
            }

            if (detailRows.Count > 0)
            {
                rows.Add(BuildShipTimingRow(detailRows));
            }
        }

        return rows
            .OrderBy(row => row.FleetId)
            .ThenBy(row => row.FleetSlot > 0 ? row.FleetSlot : 99)
            .ThenBy(row => FormationSort(row.FormationGroup))
            .ThenBy(row => row.ShipName, StringComparer.CurrentCulture)
            .ToList();
    }

    private static IEnumerable<FireRateEquipmentSlot> TimedEquipmentSlots(
        ShipState ship,
        JsonElement fireRateEquipmentMap)
    {
        return ship.Equipment
            .Where(IsTimedEquipment)
            .OrderBy(item => item.Slot)
            .Select(item => TryReadFireRateEquipment(fireRateEquipmentMap, item, out var slot) ? slot : null)
            .Where(slot => slot is not null)
            .Cast<FireRateEquipmentSlot>();
    }

    private static bool IsTimedEquipment(EquipmentItemState item)
    {
        return !item.Empty &&
               item.TemplateId > 0 &&
               item.Slot > 0 &&
               item.TypeGroup is "gun" or "torpedo" or "antiair" or "asw" or "missile";
    }

    private static string AttackRole(EquipmentItemState item)
    {
        return item.TypeGroup switch
        {
            "gun" => item.Slot == 1 ? "주포" : "부포",
            "torpedo" => "어뢰",
            "antiair" => "대공포",
            "asw" => "대잠",
            "missile" => "미사일",
            _ => item.TypeGroup
        };
    }

    private static IEnumerable<EquipmentItemState> AirstrikeItems(ShipState ship, JsonElement equipmentMap)
    {
        var equippedBySlot = ship.Equipment
            .Where(item => item.Slot > 0)
            .ToDictionary(item => item.Slot);

        for (var slot = 1; slot <= 3; slot++)
        {
            if (equippedBySlot.TryGetValue(slot, out var equipped) &&
                !equipped.Empty &&
                equipped.TemplateId > 0)
            {
                if (equipped.TypeGroup == "aircraft")
                {
                    yield return equipped;
                }

                continue;
            }

            var defaultEquipmentId = DefaultEquipmentId(ship, slot);
            if (defaultEquipmentId <= 0)
            {
                continue;
            }

            var defaultInfo = MapObject(equipmentMap, defaultEquipmentId);
            var defaultTypeGroup = DefaultEquipmentTypeGroup(ship, slot, defaultInfo);
            if (defaultTypeGroup != "aircraft")
            {
                continue;
            }

            yield return new EquipmentItemState
            {
                Slot = slot,
                TemplateId = defaultEquipmentId,
                TypeGroup = defaultTypeGroup,
                Name = FirstNonEmpty(
                    JsonString(defaultInfo, "name"),
                    $"default:{defaultEquipmentId.ToString(CultureInfo.InvariantCulture)}"),
                Rarity = RarityFilterTier(JsonString(defaultInfo, "rarity")),
                RarityClass = RarityCssClass(JsonString(defaultInfo, "rarity"))
            };
        }
    }

    private static string DefaultEquipmentTypeGroup(ShipState ship, int slot, JsonElement fallbackInfo)
    {
        var typeId = slot > 0 && slot <= ship.DefaultEquipmentTypes.Count
            ? ship.DefaultEquipmentTypes[slot - 1]
            : 0;
        if (typeId <= 0)
        {
            typeId = JsonInt(fallbackInfo, "type");
        }

        return EquipmentTypeGroup(typeId);
    }

    private static bool TryReadFireRateEquipment(
        JsonElement fireRateEquipmentMap,
        EquipmentItemState item,
        out FireRateEquipmentSlot slot)
    {
        var info = MapObject(fireRateEquipmentMap, item.TemplateId);
        var reloadMax = JsonInt(info, "reload_max");
        if (reloadMax <= 0)
        {
            slot = default!;
            return false;
        }

        var staticReloadMax = FirstPositive(
            JsonInt(info, "static_reload_max"),
            item.TypeGroup == "aircraft" ? JsonInt(info, "max_reload_max") : 0,
            reloadMax);
        slot = new FireRateEquipmentSlot(item, info, reloadMax, staticReloadMax);
        return true;
    }

    private static FleetDamageRow BuildWeaponTimingRow(
        ShipState ship,
        FleetAssignment fleet,
        FireRateEquipmentSlot slot,
        string shipTypeGroup,
        string attackRole,
        JsonElement skillMap)
    {
        var item = slot.Item;
        var info = slot.Info;
        var skillAdjustment = SkillTimingAdjustment(ship, attackRole, skillMap, info, item);
        var loadSpeed = AdjustedLoadSpeed(ship, skillAdjustment);
        var reloadMax = slot.ReloadMax * skillAdjustment.WeaponReloadRequirementMultiplier;
        var reloadSeconds = CalculateReloadSeconds(reloadMax, loadSpeed);
        var damagePerCycle = JsonInt(info, "damage_per_cycle");

        return new FleetDamageRow
        {
            FleetId = fleet.FleetId,
            FleetName = fleet.DisplayName,
            FleetSlot = fleet.SlotIndex,
            FormationGroup = FormationLabel(shipTypeGroup),
            CommanderSummary = fleet.CommanderSummary,
            ShipUid = ship.Uid,
            ShipName = ship.DisplayName,
            ShipIconUrl = ship.IconUrl,
            ShipRarity = ship.Rarity,
            ShipRarityTier = RarityFilterTier(ship.Rarity),
            ShipRarityClass = ship.RarityClass,
            ShipLevel = ship.Level,
            ShipReload = ship.Reload,
            ShipTypeId = ship.ShipTypeId,
            ShipTypeLabel = ShipTypeLabel(ship.ShipTypeId),
            ShipTypeGroup = shipTypeGroup,
            NationLabel = NationLabel(ship.NationId),
            IsProposed = ship.IsProposed,
            Slot = item.Slot,
            EquipmentId = item.TemplateId.ToString(CultureInfo.InvariantCulture),
            EquipmentName = item.Name,
            EquipmentIconUrl = item.IconUrl,
            EquipmentRarity = item.Rarity,
            EquipmentTypeGroup = item.TypeGroup,
            EnhanceLevel = item.EnhanceLevel,
            WeaponIds = string.Join(", ", JsonIntArray(info, "weapon_ids")),
            WeaponCount = JsonInt(info, "weapon_count"),
            ReloadMax = (int)Math.Round(reloadMax, MidpointRounding.AwayFromZero),
            ReloadSeconds = reloadSeconds,
            FireRatePerMinute = reloadSeconds > 0 ? 60.0 / reloadSeconds : 0,
            EffectiveLoadSpeed = loadSpeed,
            SkillAdjustment = skillAdjustment.Description,
            AttackRole = attackRole,
            AttackTimingSeconds = reloadSeconds,
            DamagePerCycle = damagePerCycle,
            EstimatedDps = EstimateDps(damagePerCycle, reloadSeconds),
            InitialOverHeat = JsonInt(info, "initial_over_heat"),
            RecoverTime = JsonInt(info, "recover_time"),
            Queue = JsonInt(info, "queue"),
            Note = FleetDamageNote(ship.Reload, attackRole, skillAdjustment.Description)
        };
    }

    private static FleetDamageRow BuildShipTimingRow(IReadOnlyCollection<FleetDamageRow> detailRows)
    {
        var orderedRows = detailRows
            .OrderBy(row => row.Slot > 0 ? row.Slot : 99)
            .ThenBy(row => row.AttackRoleText, StringComparer.CurrentCulture)
            .ToArray();
        var primary = orderedRows.First();
        var fastest = orderedRows
            .Where(row => row.ReloadSeconds > 0)
            .OrderBy(row => row.ReloadSeconds)
            .FirstOrDefault() ?? primary;
        var attackRoles = orderedRows
            .Select(row => row.AttackRoleText)
            .Where(role => role != "-")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var skillAdjustment = JoinedSkillAdjustments(orderedRows);

        return new FleetDamageRow
        {
            FleetId = primary.FleetId,
            FleetName = primary.FleetName,
            FleetSlot = primary.FleetSlot,
            FormationGroup = primary.FormationGroup,
            CommanderSummary = primary.CommanderSummary,
            ShipUid = primary.ShipUid,
            ShipName = primary.ShipName,
            ShipIconUrl = primary.ShipIconUrl,
            ShipRarity = primary.ShipRarity,
            ShipRarityTier = primary.ShipRarityTier,
            ShipRarityClass = primary.ShipRarityClass,
            ShipLevel = primary.ShipLevel,
            ShipReload = primary.ShipReload,
            ShipTypeId = primary.ShipTypeId,
            ShipTypeLabel = primary.ShipTypeLabel,
            ShipTypeGroup = primary.ShipTypeGroup,
            NationLabel = primary.NationLabel,
            IsProposed = primary.IsProposed,
            Slot = primary.Slot,
            EquipmentId = string.Join(", ", orderedRows
                .Select(row => row.EquipmentId)
                .Where(value => !string.IsNullOrWhiteSpace(value))),
            EquipmentName = $"{orderedRows.Length:N0}개 장비 타이밍",
            EquipmentIconUrl = primary.EquipmentIconUrl,
            EquipmentRarity = primary.EquipmentRarity,
            EquipmentTypeGroup = string.Join("/", orderedRows
                .Select(row => row.EquipmentTypeGroup)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)),
            WeaponIds = string.Join(", ", orderedRows
                .Select(row => row.WeaponIds)
                .Where(value => !string.IsNullOrWhiteSpace(value))),
            WeaponCount = orderedRows.Sum(row => row.WeaponCount),
            ReloadMax = fastest.ReloadMax,
            ReloadSeconds = fastest.ReloadSeconds,
            FireRatePerMinute = fastest.FireRatePerMinute,
            EffectiveLoadSpeed = fastest.EffectiveLoadSpeed,
            SkillAdjustment = skillAdjustment,
            AttackRole = string.Join("/", attackRoles),
            AttackTimingSeconds = fastest.AttackTimingSeconds,
            DamagePerCycle = orderedRows.Sum(row => row.DamagePerCycle),
            EstimatedDps = orderedRows.Sum(row => row.EstimatedDps),
            InitialOverHeat = orderedRows.Max(row => row.InitialOverHeat),
            RecoverTime = orderedRows.Max(row => row.RecoverTime),
            Queue = orderedRows.Max(row => row.Queue),
            EquipmentTimings = orderedRows,
            TimingDetailText = string.Join(Environment.NewLine, orderedRows.Select(TimingDetailLine)),
            AttackTimingSummary = string.Join(" · ", orderedRows.Select(row => $"{row.AttackRoleText} {row.ReloadSecondsText}")),
            Note = FleetDamageGroupNote(primary.ShipReload, skillAdjustment)
        };
    }

    private static FleetDamageRow BuildAirstrikeTimingRow(
        ShipState ship,
        FleetAssignment fleet,
        string shipTypeGroup,
        IReadOnlyCollection<FireRateEquipmentSlot> slots,
        JsonElement skillMap)
    {
        var skillAdjustment = SkillTimingAdjustment(ship, "공습", skillMap, default);
        var loadSpeed = AdjustedLoadSpeed(ship, skillAdjustment);
        var reloadMax = AirAssistReloadMax(ship, slots);
        var reloadSeconds = CalculateReloadSeconds(reloadMax, loadSpeed);
        var damagePerCycle = slots.Sum(slot => JsonInt(slot.Info, "damage_per_cycle"));
        var firstItem = slots.OrderBy(slot => slot.Item.Slot).First().Item;

        return new FleetDamageRow
        {
            FleetId = fleet.FleetId,
            FleetName = fleet.DisplayName,
            FleetSlot = fleet.SlotIndex,
            FormationGroup = FormationLabel(shipTypeGroup),
            CommanderSummary = fleet.CommanderSummary,
            ShipUid = ship.Uid,
            ShipName = ship.DisplayName,
            ShipIconUrl = ship.IconUrl,
            ShipRarity = ship.Rarity,
            ShipRarityTier = RarityFilterTier(ship.Rarity),
            ShipRarityClass = ship.RarityClass,
            ShipLevel = ship.Level,
            ShipReload = ship.Reload,
            ShipTypeId = ship.ShipTypeId,
            ShipTypeLabel = ShipTypeLabel(ship.ShipTypeId),
            ShipTypeGroup = shipTypeGroup,
            NationLabel = NationLabel(ship.NationId),
            IsProposed = ship.IsProposed,
            Slot = 0,
            EquipmentId = string.Join(", ", slots.Select(slot => slot.Item.TemplateId)),
            EquipmentName = $"항공 CD ({slots.Count:N0}슬롯)",
            EquipmentIconUrl = firstItem.IconUrl,
            EquipmentRarity = firstItem.Rarity,
            EquipmentTypeGroup = "aircraft",
            WeaponIds = JoinedWeaponIds(slots.Select(slot => slot.Info)),
            WeaponCount = slots.Sum(slot => JsonInt(slot.Info, "weapon_count")),
            ReloadMax = (int)Math.Round(reloadMax, MidpointRounding.AwayFromZero),
            ReloadSeconds = reloadSeconds,
            FireRatePerMinute = reloadSeconds > 0 ? 60.0 / reloadSeconds : 0,
            EffectiveLoadSpeed = loadSpeed,
            SkillAdjustment = skillAdjustment.Description,
            AttackRole = "공습",
            AttackTimingSeconds = reloadSeconds,
            DamagePerCycle = damagePerCycle,
            EstimatedDps = EstimateDps(damagePerCycle, reloadSeconds),
            InitialOverHeat = slots.Max(slot => JsonInt(slot.Info, "initial_over_heat")),
            RecoverTime = slots.Max(slot => JsonInt(slot.Info, "recover_time")),
            Queue = slots.Max(slot => JsonInt(slot.Info, "queue")),
            Note = FleetDamageNote(ship.Reload, "공습", skillAdjustment.Description)
        };
    }

    private static double AirAssistReloadMax(ShipState ship, IReadOnlyCollection<FireRateEquipmentSlot> slots)
    {
        var weighted = slots
            .Select(slot => new
            {
                ReloadMax = FirstPositive(slot.StaticReloadMax, slot.ReloadMax),
                Weight = AirSlotWeight(ship, slot.Item.Slot)
            })
            .Where(item => item.ReloadMax > 0 && item.Weight > 0)
            .ToArray();
        if (weighted.Length > 0)
        {
            var reloadTotal = weighted.Sum(item => item.ReloadMax * item.Weight);
            var weightTotal = weighted.Sum(item => item.Weight);
            return reloadTotal / (double)weightTotal * AirAssistReloadRatio;
        }

        return slots.Average(slot => FirstPositive(slot.StaticReloadMax, slot.ReloadMax)) * AirAssistReloadRatio;
    }

    private static int DefaultEquipmentId(ShipState ship, int slot)
    {
        return slot > 0 && slot <= ship.DefaultEquipmentIds.Count
            ? ship.DefaultEquipmentIds[slot - 1]
            : 0;
    }

    private static int AirSlotWeight(ShipState ship, int slot)
    {
        return slot > 0 && slot <= ship.AirSlotWeights.Count
            ? ship.AirSlotWeights[slot - 1]
            : 1;
    }

    private static double EstimateDps(int damagePerCycle, double reloadSeconds)
    {
        return reloadSeconds > 0 && damagePerCycle > 0 ? damagePerCycle / reloadSeconds : 0;
    }

    private static string TimingDetailLine(FleetDamageRow row)
    {
        var slotText = row.Slot > 0 ? $"#{row.Slot:N0}" : "";
        return string.Join(" · ", new[]
        {
            string.Join(" ", new[] { row.AttackRoleText, slotText }.Where(value => !string.IsNullOrWhiteSpace(value))),
            string.Join(" ", new[] { row.EquipmentName, row.EnhanceText }.Where(value => !string.IsNullOrWhiteSpace(value))),
            row.ReloadSecondsText,
            row.FireRateText,
            row.EstimatedDps > 0 ? $"DPS {row.EstimatedDps:N1}" : ""
        }.Where(value => !string.IsNullOrWhiteSpace(value) && value != "-"));
    }

    private static string FleetDamageGroupNote(int shipReload, string skillAdjustment)
    {
        var note = shipReload > 0
            ? string.IsNullOrWhiteSpace(skillAdjustment)
                ? "무기별 쿨다운: 스킬/명중/장갑/탄막/지휘냥 보정 제외"
                : "무기별 쿨다운: 일부 자기 스킬 보정 적용 · 명중/장갑/탄막/지휘냥 보정 제외"
            : "장전 미수집 · 기준 100";
        return AppendNote(note, skillAdjustment);
    }

    private static string FleetDamageNote(int shipReload, string attackRole, string skillAdjustment)
    {
        var note = shipReload > 0
            ? string.IsNullOrWhiteSpace(skillAdjustment)
                ? "기초 무기 DPS: 스킬/명중/장갑/탄막 보정 제외"
                : "기초 무기 DPS: 일부 자기 스킬 보정 적용 · 명중/장갑/탄막 보정 제외"
            : "장전 미수집 · 기준 100";
        var airstrikeSkillScope = string.IsNullOrWhiteSpace(skillAdjustment) ? "지휘냥/스킬 제외" : "지휘냥/기타 스킬 제외";
        var roleNote = attackRole switch
        {
            "공습" => $"{note} · 항공 CD는 슬롯 보정/기본 장비/첫 weapon×2.2 기준 · {airstrikeSkillScope}",
            "주포" => $"{note} · 사속은 슬롯 1 주포 쿨다운 기준",
            "부포" or "어뢰" or "대공포" or "대잠" or "미사일" => $"{note} · 슬롯 장비 쿨다운 기준",
            _ => note
        };
        return AppendNote(roleNote, skillAdjustment);
    }

    private static double CalculateReloadSeconds(double reloadMax, double loadSpeed)
    {
        return reloadMax / ReloadK1 / Math.Sqrt((loadSpeed + ReloadK2) * ReloadK3);
    }

    private static FireRateSkillAdjustment SkillTimingAdjustment(
        ShipState ship,
        string attackRole,
        JsonElement skillMap,
        JsonElement weaponInfo,
        EquipmentItemState? item = null)
    {
        var loadSpeedRatio = 0.0;
        var weaponReloadRequirementDelta = 0.0;
        var notes = new List<string>();

        foreach (var skill in ship.Skills)
        {
            var skillInfo = MapObject(skillMap, skill.Id);
            var fireRateInfo = Child(skillInfo, "fire_rate");
            if (fireRateInfo.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var skillName = FirstNonEmpty(JsonString(skillInfo, "name"), $"skill:{skill.Id}");
            var skillLoadSpeedRatio = LeveledDouble(
                Child(fireRateInfo, "load_speed_ratio_by_level"),
                skill.Level,
                0.0);
            if (Math.Abs(skillLoadSpeedRatio) > 0.000001)
            {
                loadSpeedRatio += skillLoadSpeedRatio;
                notes.Add($"{skillName} 장전 {SignedPercentText(skillLoadSpeedRatio)}");
            }

            foreach (var reloadRequirement in Array(fireRateInfo, "weapon_reload_requirement_deltas"))
            {
                if (!ReloadRequirementApplies(reloadRequirement, attackRole, weaponInfo, item))
                {
                    continue;
                }

                var delta = LeveledDouble(Child(reloadRequirement, "delta_by_level"), skill.Level, 0.0);
                if (Math.Abs(delta) <= 0.000001)
                {
                    continue;
                }

                weaponReloadRequirementDelta += delta;
                notes.Add($"{skillName} {ReloadRequirementLabel(reloadRequirement, attackRole, item)} {SignedPercentText(delta)}");
            }
        }

        return notes.Count == 0
            ? FireRateSkillAdjustment.None
            : new FireRateSkillAdjustment(
                Math.Max(0.01, 1.0 + loadSpeedRatio),
                Math.Max(0.01, 1.0 + weaponReloadRequirementDelta),
                $"스킬: {string.Join(" / ", notes.Distinct(StringComparer.Ordinal))}");
    }

    private static bool ReloadRequirementApplies(
        JsonElement reloadRequirement,
        string attackRole,
        JsonElement weaponInfo,
        EquipmentItemState? item)
    {
        var requiredWeaponType = JsonInt(reloadRequirement, "weapon_type");
        if (requiredWeaponType <= 0)
        {
            return true;
        }

        var weaponType = JsonInt(weaponInfo, "weapon_type");
        if (requiredWeaponType == weaponType)
        {
            return true;
        }

        return requiredWeaponType == 23 && IsMainGunAttack(attackRole, item);
    }

    private static bool IsMainGunAttack(string attackRole, EquipmentItemState? item)
    {
        return attackRole == "주포" || item is { TypeGroup: "gun", Slot: 1 };
    }

    private static string ReloadRequirementLabel(JsonElement reloadRequirement, string attackRole, EquipmentItemState? item)
    {
        var weaponType = JsonInt(reloadRequirement, "weapon_type");
        return weaponType == 23 && IsMainGunAttack(attackRole, item) ? "주포 장전시간" : $"{attackRole} 장전시간";
    }

    private static string SignedPercentText(double ratio)
    {
        var sign = ratio >= 0 ? "+" : "";
        return $"{sign}{ratio * 100.0:N1}%";
    }

    private static double AdjustedLoadSpeed(ShipState ship, FireRateSkillAdjustment skillAdjustment)
    {
        var loadSpeed = ship.Reload > 0 ? ship.Reload : 100;
        return loadSpeed * skillAdjustment.LoadSpeedMultiplier;
    }

    private static string JoinedSkillAdjustments(IEnumerable<FleetDamageRow> rows)
    {
        var notes = rows
            .Select(row => row.SkillAdjustment)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value.StartsWith("스킬: ", StringComparison.Ordinal)
                ? value["스킬: ".Length..].Split(" / ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [value])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return notes.Length == 0 ? "" : $"스킬: {string.Join(" / ", notes)}";
    }

    private static string AppendNote(string note, string detail)
    {
        return string.IsNullOrWhiteSpace(detail) ? note : $"{note} · {detail}";
    }

    private static string JoinedWeaponIds(IEnumerable<JsonElement> infos)
    {
        return string.Join(", ", infos
            .SelectMany(info => JsonIntArray(info, "weapon_ids"))
            .Distinct()
            .Order());
    }

    private static string FormationLabel(string shipTypeGroup) => shipTypeGroup switch
    {
        "main" => "후열",
        "vanguard" => "전열",
        "submarine" => "잠수",
        _ => "기타"
    };

    private static int FormationSort(string formation) => formation switch
    {
        "후열" => 0,
        "전열" => 1,
        "잠수" => 2,
        _ => 3
    };

    private static double LeveledDouble(JsonElement value, int level, double fallback)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            return fallback;
        }

        var index = Math.Clamp(level <= 0 ? 0 : level - 1, 0, value.GetArrayLength() - 1);
        return DoubleValue(value[index], fallback);
    }

    private static double DoubleValue(JsonElement value, double fallback)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetDouble(out var number) ? number : fallback;
        }

        return double.TryParse(ValueText(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static int[] JsonIntArray(JsonElement element, string propertyName)
    {
        var value = Child(element, propertyName);
        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Select(IntValue)
                .Where(item => item > 0)
                .ToArray()
            : [];
    }
}
