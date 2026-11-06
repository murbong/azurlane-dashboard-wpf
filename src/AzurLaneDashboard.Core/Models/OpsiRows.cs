namespace AzurLaneDashboard.Core.Models;

/// <summary>A sea-area target from maps/world_targets.json: its text and conditions (trigger id → count).</summary>
public sealed record OpsiTarget(int Id, string Name, string Desc, IReadOnlyList<(int Trigger, int Count)> Conditions);

/// <summary>The reward an area gives at a star count.</summary>
public sealed record OpsiAward(int Star, int Type, int ItemId, int Count);

/// <summary>A sea area with targets: normal ones first, then the hidden (cryptic) ones the game shows as ??? .</summary>
public sealed record OpsiArea(int Id, string Serial, string Region, IReadOnlyList<int> Normal, IReadOnlyList<int> Cryptic,
    IReadOnlyList<OpsiAward> Awards);

/// <summary>Operation Siren state from the last entry (SC 33001) and the target updates and claims after it.</summary>
public sealed class OpsiState
{
    public bool IsKnown { get; init; }
    public string UpdatedAt { get; init; } = "";
    /// <summary>Target id → trigger id → count.</summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<int, int>> Progress { get; init; } =
        new Dictionary<int, IReadOnlyDictionary<int, int>>();
    /// <summary>Area id → star counts whose reward was claimed.</summary>
    public IReadOnlyDictionary<int, IReadOnlySet<int>> Claimed { get; init; } = new Dictionary<int, IReadOnlySet<int>>();
    /// <summary>World item id → count in the entry snapshot.</summary>
    public IReadOnlyDictionary<int, int> Items { get; init; } = new Dictionary<int, int>();
    /// <summary>Adaptability stacks: attack (401), durability (402), recovery (403).</summary>
    public (int Attack, int Durability, int Recovery) Adaptability { get; init; }
}

/// <summary>One unfinished target in an area.</summary>
public sealed class OpsiTargetRow
{
    public int AreaId { get; init; }
    public string AreaLabel { get; init; } = "";
    public string Action { get; init; } = "";
    public string TargetName { get; init; } = "";
    public string Desc { get; init; } = "";
    public bool IsHidden { get; init; }
    public int Got { get; init; }
    public int Need { get; init; }
    public string StarsText { get; init; } = "";
    public string RewardText { get; init; } = "";

    public int Left => Math.Max(0, Need - Got);
    public double Fraction => Need <= 0 ? 0 : Math.Min(1, (double)Got / Need);
    public string ProgressText => $"{Got}/{Need}";
    public string LeftText => $"{Left}회 남음";
    public bool IsOneLeft => Left <= 1;
    public string KindText => IsHidden ? "숨은 목표" : "일반 목표";
    public string SubText => $"{StarsText} · {KindText}";
}

/// <summary>Unfinished targets that ask for the same action ("세이렌 탐지 장치 사용"), with how the game says to do it.</summary>
public sealed class OpsiActionGroup
{
    public string Action { get; init; } = "";
    public string What { get; init; } = "";
    public string How { get; init; } = "";
    public IReadOnlyList<OpsiTargetRow> Rows { get; init; } = [];

    public int LeftTotal => Rows.Sum(row => row.Left);
    public string SummaryText => $"{Rows.Select(row => row.AreaId).Distinct().Count()}개 해역 · {LeftTotal}회 남음";
}

/// <summary>An area that still lacks stars, with what is left there.</summary>
public sealed class OpsiAreaRow
{
    public string Label { get; init; } = "";
    public string StarsText { get; init; } = "";
    public string RewardText { get; init; } = "";
    public IReadOnlyList<OpsiTargetRow> Targets { get; init; } = [];
}

public sealed class OpsiClaimRow
{
    public string AreaLabel { get; init; } = "";
    public int Star { get; init; }
    public string RewardText { get; init; } = "";
    public string Text => $"{AreaLabel} · {Star}★ {RewardText}";
}

public sealed class OpsiDashboard
{
    public static OpsiDashboard Empty { get; } = new();

    public bool HasData { get; init; }
    public string BasisText { get; init; } = "";
    public int Stars { get; init; }
    public int TotalStars { get; init; }
    public int CompleteAreas { get; init; }
    public int AreaCount { get; init; }
    public IReadOnlyList<OpsiActionGroup> Groups { get; init; } = [];
    public IReadOnlyList<OpsiAreaRow> Areas { get; init; } = [];
    public IReadOnlyList<OpsiTargetRow> QuickWins { get; init; } = [];
    public IReadOnlyList<OpsiClaimRow> Claimable { get; init; } = [];
    public int EnergyContainers { get; init; }
    public int SpecialTokens { get; init; }
    /// <summary>Energy containers the unfinished device targets need: info devices, then detection devices.</summary>
    public int InfoDeviceUses { get; init; }
    public int DetectionDeviceUses { get; init; }
    public int HiddenTotal { get; init; }
    public int HiddenDone { get; init; }
    /// <summary>Unfinished hidden targets the game still shows as ??? (their area's normal targets are not all done).</summary>
    public int HiddenStillMasked { get; init; }
    public (int Attack, int Durability, int Recovery) Adaptability { get; init; }

    public int AdaptAttack => Adaptability.Attack;
    public int AdaptDurability => Adaptability.Durability;
    public int AdaptRecovery => Adaptability.Recovery;
    public int StarsLeft => TotalStars - Stars;
    public string StarsText => $"{Stars} / {TotalStars}";
    public string AreasText => $"{CompleteAreas} / {AreaCount}";
    public int ClaimableCount => Claimable.Count;
    public bool HasClaimable => Claimable.Count > 0;
    public bool HasNoClaimable => !HasClaimable;
    public bool HasQuickWins => QuickWins.Count > 0;
    /// <summary>The detection device's resource search costs two special tokens per use.</summary>
    public int DetectionTokenNeed => DetectionDeviceUses * 2;
    public string InfoNeedText => $"에너지 저장 용기 {InfoDeviceUses}개 필요 · 보유 {EnergyContainers}개";
    public string DetectionNeedText =>
        $"특별 교환 토큰 {DetectionTokenNeed}개 또는 용기 {DetectionDeviceUses}개 · 보유 토큰 {SpecialTokens}개";
    /// <summary>Info devices take containers only; detection devices are covered by tokens, else by spare containers.</summary>
    public bool CanFinishDevices => EnergyContainers >= InfoDeviceUses &&
                                    (SpecialTokens >= DetectionTokenNeed || EnergyContainers - InfoDeviceUses >= DetectionDeviceUses);
    public bool IsEnergyShort => !CanFinishDevices;
    public string DeviceVerdict => InfoDeviceUses + DetectionDeviceUses == 0
        ? "세이렌 장치 목표가 남아 있지 않습니다."
        : CanFinishDevices
            ? "지금 가진 아이템으로 장치 목표를 모두 채울 수 있습니다. 용기는 정보 수집 장치에, 토큰은 탐지 장치에 쓰세요."
            : EnergyContainers < InfoDeviceUses
                ? $"정보 수집 장치에 쓸 용기가 {InfoDeviceUses - EnergyContainers}개 모자랍니다."
                : $"탐지 장치에 쓸 토큰이 {DetectionTokenNeed - SpecialTokens}개 모자랍니다.";
    public string HiddenText => HiddenStillMasked > 0
        ? $"숨은 목표 {HiddenTotal}개 중 {HiddenDone}개를 달성했습니다. 남은 것 중 {HiddenStillMasked}개는 해역의 일반 목표를 다 끝내기 전이라 게임에서 ???로 보입니다."
        : $"숨은 목표 {HiddenTotal}개 중 {HiddenDone}개를 달성했습니다. 게임은 해역의 일반 목표를 다 끝내야 숨은 목표를 보여 주는데, 남은 숨은 목표는 모두 이미 보이는 상태입니다.";
}
