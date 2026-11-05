using System.Globalization;

namespace AzurLaneDashboard.Core.Models;

public sealed class FleetPlanDashboard
{
    public IReadOnlyList<FleetPlanContent> Contents { get; init; } = [];
    public IReadOnlyList<CommunityRatingChangeRow> RatingChanges { get; init; } = [];
    public string RatingSourceText { get; init; } = "";
    public int OwnedShipCount { get; init; }
    public int ReadyShipCount { get; init; }
    public int ReadyLevel { get; init; } = 120;
    public FleetPlannerInputs Inputs { get; init; } = new();

    public FleetPlanDashboard WithInputs(FleetPlannerInputs inputs) => new()
    {
        Contents = Contents,
        RatingChanges = RatingChanges,
        RatingSourceText = RatingSourceText,
        OwnedShipCount = OwnedShipCount,
        ReadyShipCount = ReadyShipCount,
        ReadyLevel = ReadyLevel,
        Inputs = inputs
    };

    public bool HasContents => Contents.Any(content => content.ShipCount > 0);
    public bool HasRatingChanges => RatingChanges.Count > 0;

    public string RosterText => string.Create(CultureInfo.CurrentCulture,
        $"보유 {OwnedShipCount:N0}척 · Lv.{ReadyLevel} 이상 {ReadyShipCount:N0}척");
}

public sealed class FleetPlanContent
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string Group { get; init; } = "";
    public IReadOnlyList<FleetPlanFleet> Fleets { get; init; } = [];
    public IReadOnlyList<FleetPlanCandidate> Candidates { get; init; } = [];
    public IReadOnlyList<FleetPlanFinishItem> FinishItems { get; init; } = [];

    public int ShipCount => Fleets.Sum(fleet => fleet.ShipCount);
    public bool HasFleets => ShipCount > 0;
    public bool HasCandidates => Candidates.Count > 0;
    public bool HasFinishItems => FinishItems.Count > 0;
    public IReadOnlyList<FleetPlanFinishItem> EquipmentGaps { get; init; } = [];
    public bool HasEquipmentGaps => EquipmentGaps.Count > 0;
    public string EquipmentGapText => $"장비 빈칸이 있는 함선 {EquipmentGaps.Count}척 · 편성하기 전에 장비를 채우세요";
    public string EquipmentGapToolTip => string.Join(Environment.NewLine,
        EquipmentGaps.Select(item => $"{item.Name} · {item.Detail}"));
    public bool IsFleetFinished => HasFleets && !HasFinishItems;

    public FleetPlanCandidate? TopCandidate => Candidates.FirstOrDefault();
    public IReadOnlyList<FleetPlanCandidate> OtherCandidates => Candidates.Skip(1).ToList();
    public bool HasOtherCandidates => Candidates.Count > 1;
    public string CandidateCountText => HasCandidates ? $"후보 {Candidates.Count}척" : "";

    public string CompletionText => IsFleetFinished
        ? $"✓ 편성 {ShipCount}척 모두 육성 완료"
        : $"편성 {ShipCount}척 중 {FinishItems.Count}척 마무리 필요";
}

public sealed class FleetPlanFleet
{
    public string Title { get; init; } = "";
    public string ContextLabel { get; init; } = "";
    public IReadOnlyList<FleetPlanSlot> MainSlots { get; init; } = [];
    public IReadOnlyList<FleetPlanSlot> VanguardSlots { get; init; } = [];
    public int GameFleetOverlap { get; init; }

    public int ShipCount => MainSlots.Count + VanguardSlots.Count;
    private IEnumerable<FleetPlanSlot> Slots => MainSlots.Concat(VanguardSlots);

    public string RoleSummary =>
        $"역할 · 공격 버프 {Slots.Count(slot => slot.HasOffenseBuff)} · 방어 {Slots.Count(slot => slot.HasDefense)} · 대공 {Slots.Count(slot => slot.HasAntiAir)}";

    public bool MatchesGameFleet => ShipCount > 0 && GameFleetOverlap >= ShipCount;
    public bool DiffersFromGameFleet => !MatchesGameFleet && GameFleetOverlap * 3 <= ShipCount;

    public string OverlapText => MatchesGameFleet
        ? "게임 편성과 동일"
        : $"게임 편성과 {GameFleetOverlap}/{ShipCount}척 같음";
}

public sealed class FleetPlanSlot
{
    public string ShipUid { get; init; } = "";
    public string Name { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string HullLabel { get; init; } = "";
    public int Level { get; init; }
    public string Rating { get; init; } = "";
    public IReadOnlyList<string> Roles { get; init; } = [];
    public string PartnerName { get; init; } = "";
    public string UpgradeCandidateName { get; init; } = "";
    public bool HasOffenseBuff { get; init; }
    public bool HasDefense { get; init; }
    public bool HasAntiAir { get; init; }
    /// <summary>Where the ship stands when the order is a placement: "선두 · 생존력 97", later the flagship.</summary>
    public string PositionText { get; init; } = "";

    public bool HasPositionText => PositionText.Length > 0;
    public string RatingTier => FleetPlanRating.Tier(Rating);
    public bool IsConditionalRating => Rating.Contains('*', StringComparison.Ordinal);
    public string RatingToolTip => IsConditionalRating
        ? "커뮤니티 등급 · 특정 보스·상황에서만 이 등급입니다"
        : "커뮤니티 등급";
    // Level first: narrow tiles trim the hull label, never the level.
    public string SubText => string.IsNullOrWhiteSpace(HullLabel) ? $"Lv.{Level}" : $"Lv.{Level} · {HullLabel}";
    public bool HasRoles => Roles.Count > 0;
    public bool HasPartner => !string.IsNullOrWhiteSpace(PartnerName);
    public string PartnerText => $"짝 · {PartnerName}";
    public bool HasUpgrade => !string.IsNullOrWhiteSpace(UpgradeCandidateName);
    public string UpgradeText => $"↑ 교체 후보 · {UpgradeCandidateName}";

    public string ToolTipText => string.Join(Environment.NewLine, new[]
    {
        $"{Name} · {SubText}",
        $"커뮤니티 등급 {Rating}" + (IsConditionalRating ? " (특정 보스·상황 한정)" : ""),
        HasPositionText ? $"자리: {PositionText}" + (PositionText.StartsWith("선두", StringComparison.Ordinal)
            ? " (Regal 생존력이 가장 높아 전위 맨 앞)"
            : " (Regal 기준 기함이 아니면 스킬·탄막 일부를 잃음)") : "",
        HasRoles ? $"역할: {string.Join(", ", Roles)}" : "",
        HasPartner ? $"짝: {PartnerName}와 함께 편성할 때의 등급" : "",
        HasUpgrade ? $"교체 후보: {UpgradeCandidateName}" : ""
    }.Where(line => line.Length > 0));
}

public sealed class FleetPlanCandidate
{
    public int Rank { get; init; }
    public string ShipUid { get; init; } = "";
    public string Name { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string HullLabel { get; init; } = "";
    public int Level { get; init; }
    public int TargetLevel { get; init; } = 120;
    public string FleetTitle { get; init; } = "";
    public string PositionLabel { get; init; } = "";
    public string ReplacesUid { get; init; } = "";
    public string ReplacesName { get; init; } = "";
    public string ReplacesRating { get; init; } = "";
    public string Rating { get; init; } = "";
    public int? DamageDeltaPercent { get; init; }
    public int ContentReach { get; init; }
    public int? ExperienceNeeded { get; init; }
    public int? EstimatedBattles { get; init; }
    public int SampleExp { get; init; }

    public bool IsTop => Rank == 1;
    public string RankText => Rank.ToString(CultureInfo.InvariantCulture);
    public string TitleSubText => string.IsNullOrWhiteSpace(HullLabel)
        ? $"Lv.{Level} → {TargetLevel}"
        : $"{HullLabel} · Lv.{Level} → {TargetLevel}";
    public string EffectText => $"{FleetTitle} {PositionLabel}";
    public string ReplacesText => string.IsNullOrWhiteSpace(ReplacesName) ? "빈 자리를 채움" : $"{ReplacesName} 대신";
    public bool HasDamageGain => DamageDeltaPercent > 0;
    public string DamageText => $"피해량 +{DamageDeltaPercent ?? 0}%";
    public bool RatingImproves => FleetPlanRating.Score(Rating) > FleetPlanRating.Score(ReplacesRating);
    public string RatingText => string.IsNullOrWhiteSpace(ReplacesRating)
        ? $"등급 {Rating}"
        : $"등급 {ReplacesRating} → {Rating}";
    public bool HasReach => ContentReach > 1;
    public string ReachText => $"{ContentReach}개 콘텐츠에서 편성";

    /// <summary>The single strongest reason, so the list never needs a row of badges.</summary>
    public string GainText => HasDamageGain ? $"피해량 +{DamageDeltaPercent}%"
        : RatingImproves ? RatingText
        : string.IsNullOrWhiteSpace(ReplacesName) ? "빈 자리" : "";

    public string ReachSuffix => HasReach ? $" · {ContentReach}개 콘텐츠에서 편성" : "";
    public double LevelProgress => TargetLevel <= 0 ? 0 : Math.Clamp(Level * 100.0 / TargetLevel, 0, 100);

    public string CostText
    {
        get
        {
            if (ExperienceNeeded is not { } experience)
                return "필요 EXP 정보 확인 필요";
            var text = $"EXP {FleetPlanRating.FormatTenThousands(experience)}";
            return EstimatedBattles is { } battles
                ? string.Create(CultureInfo.CurrentCulture, $"{text} · 약 {battles:N0}전투")
                : text;
        }
    }

    public string BasisText => EstimatedBattles is null
        ? "전투 표본 없음"
        : string.Create(CultureInfo.CurrentCulture, $"최근 평균 {SampleExp:N0} EXP");
}

public sealed class FleetPlanFinishItem
{
    public string ShipUid { get; init; } = "";
    public string Name { get; init; } = "";
    public string Detail { get; init; } = "";
    public int Level { get; init; }
}

public sealed class CommunityRatingChangeRow
{
    public string DateText { get; init; } = "";
    public string ShipText { get; init; } = "";
    public IReadOnlyList<CommunityRatingChangePart> Parts { get; init; } = [];
    public string OriginalText { get; init; } = "";
}

public sealed class CommunityRatingChangePart
{
    public string Label { get; init; } = "";
    public string From { get; init; } = "";
    public string To { get; init; } = "";

    public bool HasDelta => From.Length > 0 && To.Length > 0;
    public bool IsUp => FleetPlanRating.Score(To) > FleetPlanRating.Score(From);
    public string DeltaText => $"{From} → {To}";
}

public static class FleetPlanRating
{
    public static int Score(string? rating)
    {
        var normalized = (rating ?? "").Trim().ToUpperInvariant().Replace("*", "", StringComparison.Ordinal);
        return normalized switch
        {
            "EX" => 60,
            "SS" => 52,
            "S+" => 48,
            "S" => 44,
            "A+" => 40,
            "A" => 34,
            "B" => 22,
            "C" => 10,
            _ => 0
        };
    }

    public static string Tier(string? rating) => Score(rating) switch
    {
        >= 52 => "top",
        >= 44 => "high",
        >= 34 => "mid",
        _ => "low"
    };

    public static string FormatTenThousands(int value) => value >= 10_000
        ? string.Create(CultureInfo.CurrentCulture, $"{Math.Round(value / 10_000.0, MidpointRounding.AwayFromZero):N0}만")
        : value.ToString("N0", CultureInfo.CurrentCulture);
}
