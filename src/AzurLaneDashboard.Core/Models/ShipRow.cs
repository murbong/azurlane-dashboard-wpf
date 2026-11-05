namespace AzurLaneDashboard.Core.Models;

public sealed class ShipRow
{
    private IReadOnlyList<ShipEquipmentSlotRow>? loadoutSlots;
    private IReadOnlyList<ShipStatusBadgeRow>? statusBadges;
    private string? detailHeaderSummary;
    private string? searchText;
    private string? shipMetaSummary;

    public string ShipUid { get; init; } = "";
    public string TemplateId { get; init; } = "";
    public string Name { get; init; } = "";
    public string CustomName { get; init; } = "";
    public string DisplayName => string.IsNullOrWhiteSpace(CustomName) ? Name : $"{Name} ({CustomName})";
    public bool HasCustomName => !string.IsNullOrWhiteSpace(CustomName);
    public string CustomNameText => HasCustomName ? $"애칭 {CustomName}" : "";
    public string ShipDisplayName => Name;

    public string ShipDisplaySummary => string.Join(" · ",
        new[] { CustomNameText, ShipMetaSummary }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string ShipDisplayIconUrl => IconUrl;

    public string ShipMetaSummary => shipMetaSummary ??= string.Join(" · ",
        new[] { ShipTypeLabel, NationLabel }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    public string DetailHeaderSummary => detailHeaderSummary ??= string.Join(" · ",
        new[] { ShipTypeLabel, NationLabel, $"Lv.{Level:N0}" }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string IconUrl { get; init; } = "";
    public string BackgroundUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string RarityTier { get; init; } = "";
    public string RarityClass { get; init; } = "";

    public int RaritySortScore => RarityTier switch
    {
        "UR" => 70,
        "SSR" => 52,
        "SR" => 32,
        "R" => 16,
        "N" => 4,
        _ => 0
    };

    public int Level { get; init; }
    public int LimitBreak { get; init; }
    public int EnhanceLevel { get; init; }
    public string EnhanceText { get; init; } = "-";
    public int Exp { get; init; }
    public int ExpCurrent { get; init; }
    public int ExpRequired { get; init; }
    public double ExpPercent { get; init; }
    public bool ExpIsMax { get; init; }
    public int Intimacy { get; init; }
    public int MoodValue { get; init; }
    public string MoodLabel { get; init; } = "";
    public string MoodText => MoodValue > 0 ? $"{MoodValue:N0}" : "-";
    public int CreateTime { get; init; }

    public string CreateTimeText
    {
        get
        {
            var value = CreateTime;
            if (value <= 0)
            {
                return "-";
            }

            return DateTimeOffset.FromUnixTimeSeconds(value).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        }
    }

    public string StatsSummary { get; init; } = "";
    public string StatsDetail { get; init; } = "";
    public IReadOnlyList<ShipStatRow> StatRows { get; init; } = [];
    public int Reload { get; init; }
    public int ShipTypeId { get; init; }
    public string ShipTypeLabel { get; init; } = "";
    public string ShipTypeGroup { get; init; } = "";
    public int NationId { get; init; }
    public string NationLabel { get; init; } = "";
    public int SkinId { get; init; }
    public string SkinName { get; init; } = "";
    public bool Locked { get; init; }
    public bool IsProposed { get; init; }
    public string EquippedSummary { get; init; } = "";
    public string SpecialEquipmentSummary { get; init; } = "";
    public string SkillSummary { get; init; } = "";
    public int SkillGap { get; init; }
    public int EmptySlots { get; init; }
    public string FleetLabel { get; init; } = "";
    public string FleetTechGetBonus { get; init; } = "";
    public string FleetTechLevelBonus { get; init; } = "";
    public bool FleetTechLevelPending { get; init; }
    public bool FleetTechLevelReloadPending { get; init; }
    public string CommunityLabel { get; init; } = "";
    public string CommunitySummary { get; init; } = "";
    public int CommunityScore { get; init; }
    public int CommunityBestScore { get; init; }

    public string CommunityKind => CommunityBestScore >= 34 ? "high" :
        CommunityBestScore >= 22 ? "mid" :
        CommunityBestScore > 0 ? "low" : "";

    public int? RunsTo120Avg { get; init; }
    public int? RunsTo125Avg { get; init; }
    public int RecentBattleExpAvg { get; init; }
    public bool HasFullState { get; init; }
    public bool IsBulin { get; init; }
    public IReadOnlyList<ShipEquipmentSlotRow> EquipmentSlots { get; init; } = [];
    public ShipEquipmentSlotRow? SpecialEquipmentSlot { get; init; }

    public ShipEquipmentSlotRow DisplaySpecialEquipmentSlot =>
        SpecialEquipmentSlot ?? ShipEquipmentSlotRow.EmptySpecialEquipmentSlot;

    public IReadOnlyList<ShipEquipmentSlotRow> LoadoutSlots =>
        loadoutSlots ??= EquipmentSlots.Concat([DisplaySpecialEquipmentSlot]).ToArray();

    public IReadOnlyList<ShipSkillDetailRow> SkillDetails { get; init; } = [];
    public IReadOnlyList<ShipCommunityRatingRow> CommunityRatings { get; init; } = [];
    // One line of chips in list rows; the full list stays in the tooltip and detail panel.
    public IReadOnlyList<ShipCommunityRatingRow> CommunityRatingPreview => CommunityRatings.Take(3).ToList();
    public IReadOnlyList<ShipRecommendationGroupRow> GearRecommendationGroups { get; init; } = [];
    public IReadOnlyList<ShipRecommendationRow> AugmentRecommendations { get; init; } = [];
    public int GearRecommendationOwned { get; init; }
    public int GearRecommendationTotal { get; init; }
    public int AugmentRecommendationOwned { get; init; }
    public int AugmentRecommendationTotal { get; init; }

    public IReadOnlyList<ShipStatusBadgeRow> StatusBadges => statusBadges ??= BuildStatusBadges();

    private IReadOnlyList<ShipStatusBadgeRow> BuildStatusBadges()
    {
        var badges = new List<ShipStatusBadgeRow>
        {
            Locked ? new ShipStatusBadgeRow("잠금", "info") : new ShipStatusBadgeRow("미잠금", "warn")
        };
        // Stored ships are usually stripped on purpose and their slot icons already show the gaps,
        // so the warning is kept for ships that sortie with missing equipment.
        if (EmptySlots > 0 && !string.IsNullOrWhiteSpace(FleetLabel))
        {
            badges.Add(new ShipStatusBadgeRow($"편성 빈칸 {EmptySlots:N0}", "warn"));
        }

        if (SkillGap > 0)
        {
            badges.Add(new ShipStatusBadgeRow("스킬", "info"));
        }

        if (FleetTechLevelReloadPending)
        {
            badges.Add(new ShipStatusBadgeRow("120 장전", "good"));
        }
        else if (FleetTechLevelPending)
        {
            badges.Add(new ShipStatusBadgeRow("120 기술", "good"));
        }

        if (Level < 120)
        {
            badges.Add(new ShipStatusBadgeRow("레벨", "warn"));
        }

        return badges;
    }

    public string RecentBattleSummary
    {
        get
        {
            if (RecentBattleExpAvg <= 0 && RunsTo120Avg is null && RunsTo125Avg is null)
            {
                return "표본 없음";
            }

            var runs120 = RunsTo120Avg is null ? "-" : RunsTo120Avg == 0 ? "완료" : $"{RunsTo120Avg:N0}판";
            var runs125 = RunsTo125Avg is null ? "-" : RunsTo125Avg == 0 ? "완료" : $"{RunsTo125Avg:N0}판";
            return $"{RecentBattleExpAvg:N0} EXP/판 · 120까지 {runs120} · 125까지 {runs125}";
        }
    }

    public string RecommendationSummary
    {
        get
        {
            if (GearRecommendationTotal <= 0 && AugmentRecommendationTotal <= 0)
            {
                return "커뮤니티 추천 없음";
            }

            return
                $"장비 {GearRecommendationOwned:N0}/{GearRecommendationTotal:N0} · SP {AugmentRecommendationOwned:N0}/{AugmentRecommendationTotal:N0}";
        }
    }

    public string GearRecommendationSummary => GearRecommendationTotal <= 0
        ? "커뮤니티 장비 추천 없음"
        : $"보유 {GearRecommendationOwned:N0}/{GearRecommendationTotal:N0}";

    public string AugmentRecommendationSummary => AugmentRecommendationTotal <= 0
        ? "커뮤니티 특수장비 추천 없음"
        : $"보유 {AugmentRecommendationOwned:N0}/{AugmentRecommendationTotal:N0}";

    public string SearchText => searchText ??=
        $"{ShipUid} {TemplateId} {DisplayName} {Rarity} {RarityTier} {SkinName} {ShipTypeLabel} {NationLabel} {CommunityLabel} {StatsSummary} {string.Join(" ", StatRows.Select(row => row.SearchText))}"
            .ToLowerInvariant();
}

public sealed record ShipStatRow(string Label, string ValueText, int SortOrder)
{
    public string SearchText => $"{Label} {ValueText}";
}

public sealed class ShipEquipmentSlotRow
{
    public static ShipEquipmentSlotRow EmptySpecialEquipmentSlot { get; } = new()
    {
        Slot = 0,
        Empty = true,
        Rarity = "unknown",
        RarityClass = "rarity-unknown"
    };

    public int Slot { get; init; }
    public bool Empty { get; init; }
    public bool IsSpecial => Slot <= 0;
    public string TemplateId { get; init; } = "";
    public string Name { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string SkinIconUrl { get; init; } = "";
    public string DisplayIconUrl => Empty && !string.IsNullOrWhiteSpace(SkinIconUrl) ? SkinIconUrl : IconUrl;
    public bool HasSkinIcon => !string.IsNullOrWhiteSpace(SkinIconUrl);
    public bool ShowSkinThumbnail => !Empty && HasSkinIcon;
    public string Rarity { get; init; } = "";
    public string RarityClass { get; init; } = "";
    public int EnhanceLevel { get; init; }
    public string TypeGroup { get; init; } = "";

    public string TypeLabel => TypeGroup switch
    {
        "gun" => "함포",
        "aircraft" => "항공",
        "torpedo" => "어뢰",
        "antiair" => "대공",
        "auxiliary" => "설비",
        "asw" => "대잠",
        "missile" => "미사일",
        _ => TypeGroup
    };

    public IReadOnlyList<EquipmentAttributeRow> Attributes { get; init; } = [];
    public string AttributeSummary => EquipmentAttributeRow.Join(Attributes);
    public string SkinName { get; init; } = "";
    public string SlotLabel => Slot > 0 ? $"슬롯 {Slot}" : "특수장비";
    public string SlotTag => Slot > 0 ? Slot.ToString() : "SP";
    public string SkinTag => string.IsNullOrWhiteSpace(SkinName) ? "" : "SK";
    public string EnhanceText => Empty ? "" : $"+{EnhanceLevel:N0}";

    public string DisplayName => Empty
        ? string.IsNullOrWhiteSpace(SkinName)
            ? IsSpecial ? "특수장비 빈칸" : "빈칸"
            : $"스킨: {SkinName}"
        : Name;

    public string Detail => Empty
        ? IsSpecial ? "특수장비 없음" : "장비 없음"
        : string.Join(" · ",
            new[]
            {
                TypeLabel,
                AttributeSummary,
                string.IsNullOrWhiteSpace(SkinName) ? "" : $"스킨 {SkinName}"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string ToolTipText => string.Join(Environment.NewLine,
        new[]
        {
            SlotLabel,
            DisplayName,
            Empty ? "" : $"+{EnhanceLevel:N0}",
            string.IsNullOrWhiteSpace(Rarity) ? "" : $"등급 {Rarity}",
            Detail
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed class ShipSkillDetailRow
{
    public int Index { get; init; }
    public string SkillId { get; init; } = "";
    public string Name { get; init; } = "";
    public int? MaxLevel { get; init; }
    public int Level { get; init; }
    public int Exp { get; init; }
    public string Title => string.IsNullOrWhiteSpace(Name) ? $"스킬 {Index:N0}" : Name;
    public string Detail => MaxLevel is null ? $"최대 레벨 정보 없음 · EXP {Exp:N0}" : $"최대 Lv.{MaxLevel} · EXP {Exp:N0}";
    public string LevelLabel => $"Lv.{Level:N0}";
    public string Kind => MaxLevel is null ? "info" : Level >= MaxLevel ? "good" : "warn";
}

public sealed record ShipCommunityRatingRow(string Context, string Rating, int Score)
{
    public string Label => $"{Context} {Rating}";
    public string Kind => Score >= 34 ? "high" : Score >= 22 ? "mid" : Score > 0 ? "low" : "";
}

public sealed record ShipStatusBadgeRow(string Label, string Kind);

public sealed class ShipRecommendationGroupRow
{
    public int Slot { get; init; }
    public string SlotLabel => Slot > 0 ? $"{Slot:N0}번 슬롯" : "공용 슬롯";
    public int OwnedCount { get; init; }
    public int TotalCount { get; init; }
    public int HiddenCount { get; init; }
    public IReadOnlyList<ShipRecommendationRow> Items { get; init; } = [];
    public string Summary => $"보유 {OwnedCount:N0} / 후보 {TotalCount:N0}";
    public string HiddenText => HiddenCount > 0 ? $"외 {HiddenCount:N0}개 후보" : "";
}

public sealed class ShipRecommendationRow
{
    public string Kind { get; init; } = "";
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string LocalName { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string RarityClass { get; init; } = "";
    public bool Owned { get; init; }
    public int OwnedCount { get; init; }
    public int OwnedEnhanceLevel { get; init; }
    public int OriginalRank { get; init; }
    public string UsecaseSummary { get; init; } = "";
    public string ConditionSummary { get; init; } = "";
    public string NoteSummary { get; init; } = "";

    public string RankLabel => OriginalRank > 0 ? $"#{OriginalRank:N0}" : "";
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;

    public string LocalNameSuffix => !string.IsNullOrWhiteSpace(LocalName) &&
                                     !string.Equals(LocalName, Name, StringComparison.Ordinal)
        ? $" · {LocalName}"
        : "";

    public string DisplayTitle => $"{DisplayName}{LocalNameSuffix}";

    public string Detail => string.Join(" · ",
        new[] { UsecaseSummary, ConditionSummary, NoteSummary }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string StateText => Owned
        ? $"보유 {Math.Max(OwnedCount, 1):N0} · +{OwnedEnhanceLevel:N0}"
        : OriginalRank > 0
            ? $"미보유 · 보유 시 {RankLabel}"
            : "미보유";

    public string StateKind => Owned ? "good" : "warn";

    public string ToolTipText => string.Join(Environment.NewLine,
        new[]
        {
            DisplayName + LocalNameSuffix,
            string.IsNullOrWhiteSpace(Rarity) ? "" : $"등급 {Rarity}",
            string.IsNullOrWhiteSpace(Detail) ? "" : Detail,
            StateText
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
