using System.Globalization;

namespace AzurLaneDashboard.Core.Models;

public sealed class FleetDamageRow
{
    private string? searchText;
    private IReadOnlyList<FleetDamageRow>? displayEquipmentTimings;

    public int FleetId { get; init; }
    public string FleetName { get; init; } = "";
    public int FleetSlot { get; init; }
    public string FormationGroup { get; init; } = "";
    public string CommanderSummary { get; init; } = "";
    public string ShipUid { get; init; } = "";
    public string ShipName { get; init; } = "";
    public string ShipIconUrl { get; init; } = "";
    public string ShipRarity { get; init; } = "";
    public string ShipRarityTier { get; init; } = "";
    public string ShipRarityClass { get; init; } = "";
    public string Rarity => ShipRarity;
    public string ShipDisplayName => ShipName;
    public string ShipDisplayIconUrl => ShipIconUrl;
    public string ShipDisplaySummary => ShipSummary;
    public int ShipLevel { get; init; }
    public int ShipReload { get; init; }
    public int ShipTypeId { get; init; }
    public string ShipTypeLabel { get; init; } = "";
    public string ShipTypeGroup { get; init; } = "";
    public string NationLabel { get; init; } = "";
    public bool IsProposed { get; init; }
    public int Slot { get; init; }
    public string EquipmentId { get; init; } = "";
    public string EquipmentName { get; init; } = "";
    public string EquipmentIconUrl { get; init; } = "";
    public string EquipmentRarity { get; init; } = "";
    public string EquipmentTypeGroup { get; init; } = "";
    public string EquipmentTypeLabel => EquipmentTypeGroup switch
    {
        "gun" => "함포",
        "aircraft" => "항공",
        "torpedo" => "어뢰",
        "antiair" => "대공",
        "auxiliary" => "설비",
        "asw" => "대잠",
        "missile" => "미사일",
        _ => string.IsNullOrWhiteSpace(EquipmentTypeGroup) ? "-" : EquipmentTypeGroup
    };

    public string EquipmentTypeIcon => EquipmentTypeGroup switch
    {
        "gun" => "⌖",
        "aircraft" => "✈",
        "torpedo" => "⇢",
        "antiair" => "✹",
        "auxiliary" => "◆",
        "asw" => "◌",
        "missile" => "↟",
        _ => "•"
    };

    public int EnhanceLevel { get; init; }
    public string WeaponIds { get; init; } = "";
    public int WeaponCount { get; init; }
    public int ReloadMax { get; init; }
    public double ReloadSeconds { get; init; }
    public double FireRatePerMinute { get; init; }
    public double EffectiveLoadSpeed { get; init; }
    public string SkillAdjustment { get; init; } = "";
    public string AttackRole { get; init; } = "";
    public double AttackTimingSeconds { get; init; }
    public int DamagePerCycle { get; init; }
    public double EstimatedDps { get; init; }
    public int InitialOverHeat { get; init; }
    public int RecoverTime { get; init; }
    public int Queue { get; init; }
    public string TimingDetailText { get; init; } = "";
    public string AttackTimingSummary { get; init; } = "";
    public string Note { get; init; } = "";
    public IReadOnlyList<FleetDamageRow> EquipmentTimings { get; init; } = [];
    public IReadOnlyList<FleetDamageRow> DisplayEquipmentTimings =>
        displayEquipmentTimings ??= EquipmentTimings.Count > 0 ? EquipmentTimings : [this];

    public string SlotLabel => Slot > 0 ? $"#{Slot:N0}" : AttackRoleText;
    public string ShipLevelText => ShipLevel > 0 ? $"Lv.{ShipLevel:N0}" : "-";
    public string ShipReloadText => ShipReload > 0 ? ShipReload.ToString("N0", CultureInfo.InvariantCulture) : "-";
    public string EffectiveLoadSpeedText => EffectiveLoadSpeed > 0
        ? EffectiveLoadSpeed.ToString("N1", CultureInfo.InvariantCulture)
        : "-";
    public string ReloadMaxText => ReloadMax > 0 ? ReloadMax.ToString("N0", CultureInfo.InvariantCulture) : "-";
    public string ReloadSecondsText => ReloadSeconds > 0 ? $"{ReloadSeconds:N2}s" : "-";
    public string ReloadPerShotText => ReloadSeconds > 0 ? $"{ReloadSeconds:N2}s/회" : "-";
    public string FireRateText => FireRatePerMinute > 0 ? $"{FireRatePerMinute:N2}/분" : "-";
    public string AttackRoleText => string.IsNullOrWhiteSpace(AttackRole) ? "-" : AttackRole;

    public string AttackTimingText =>
        AttackTimingSeconds > 0 ? $"{AttackTimingSeconds:N2}s" : "-";

    public string FleetPositionText => string.Join(" · ",
        new[]
        {
            FleetSlot > 0 ? $"#{FleetSlot:N0}" : "",
            FormationGroup
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string CommanderSummaryText => string.IsNullOrWhiteSpace(CommanderSummary) ? "-" : CommanderSummary;

    public string DamageText => DamagePerCycle > 0 ? DamagePerCycle.ToString("N0", CultureInfo.InvariantCulture) : "-";
    public string EstimatedDpsText => EstimatedDps > 0 ? $"{EstimatedDps:N1}" : "-";
    public string EnhanceText => EnhanceLevel > 0 ? $"+{EnhanceLevel:N0}" : "";

    public string EquipmentSummary => string.Join(" · ",
        new[] { EquipmentName, EnhanceText }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    public string ShipSummary => string.Join(" · ",
        new[] { ShipLevelText, ShipTypeLabel, NationLabel }
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "-"));

    public string EquipmentDetail => string.Join(" · ",
        new[]
        {
            EquipmentTypeLabel,
            string.IsNullOrWhiteSpace(WeaponIds) ? "" : $"weapon {WeaponIds}"
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string CooldownTooltip => string.Join(Environment.NewLine,
        new[]
        {
            EquipmentSummary,
            EquipmentDetail,
            EffectiveLoadSpeed > 0 && Math.Abs(EffectiveLoadSpeed - ShipReload) > 0.05
                ? $"적용 장전 {EffectiveLoadSpeedText}"
                : "",
            $"쿨다운 {ReloadPerShotText}",
            $"사속 {FireRateText}",
            AttackTimingSeconds > 0 ? $"타이밍 {AttackRole} {AttackTimingSeconds:N2}s" : "",
            DamagePerCycle > 0 ? $"기초 피해 {DamagePerCycle:N0}" : "",
            EstimatedDps > 0 ? $"DPS {EstimatedDps:N1}" : "",
            SkillAdjustment,
            TimingDetailText
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string TimingSummary => string.Join(" · ",
        new[]
        {
            ReloadSecondsText,
            FireRateText,
            AttackTimingSeconds > 0 ? $"{AttackRole} {AttackTimingSeconds:N2}s" : "",
            DamagePerCycle > 0 ? $"기초 피해 {DamagePerCycle:N0}" : "",
            EstimatedDps > 0 ? $"DPS {EstimatedDps:N1}" : ""
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string TimingDetailDisplay => string.IsNullOrWhiteSpace(TimingDetailText)
        ? TimingSummary
        : TimingDetailText;

    public string AttackTimingSummaryText => string.IsNullOrWhiteSpace(AttackTimingSummary)
        ? AttackTimingSeconds > 0 ? $"{AttackRole} {AttackTimingSeconds:N2}s" : ""
        : AttackTimingSummary;

    public string SearchText => searchText ??= (
            $"{FleetId} {FleetName} {FleetPositionText} {CommanderSummary} {ShipUid} {ShipName} {ShipRarity} {ShipRarityTier} {ShipTypeLabel} {ShipTypeGroup} {NationLabel} " +
            $"{EquipmentId} {EquipmentName} {EquipmentRarity} {EquipmentTypeGroup} {AttackRole} {WeaponIds} {SkillAdjustment} {TimingDetailText}")
        .ToLowerInvariant();
}
