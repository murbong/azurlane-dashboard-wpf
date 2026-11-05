using AzurLaneDashboard.Core.Models;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.App.ViewModels;

public sealed partial class FleetPlannerViewModel
{
    private int savedBossId;
    private MetaBossChoice? selectedBoss;
    private MetaBossPlan? metaPlan;
    private string selectedMetaShipUid = "";
    private GearSourceMode gearMode;
    private GearPanel metaGearPanel = GearPanel.Empty;

    public BulkObservableCollection<MetaBossChoice> BossChoices { get; } = [];
    public RelayCommand SelectMetaShipCommand { get; }
    public RelayCommand SetGearModeCommand { get; }

    public MetaBossPlan? MetaPlan => metaPlan;
    public bool HasMetaPlan => metaPlan?.Slots.Any() == true;
    public bool HasNoMetaPlan => !HasMetaPlan;
    public int SavedBossId => savedBossId;
    public string GearModeKey => gearMode == GearSourceMode.Bag ? "bag" : "move";
    public bool IsGearMoveMode => gearMode == GearSourceMode.Move;
    public bool IsGearBagMode => gearMode == GearSourceMode.Bag;
    public string SelectedMetaShipUid => selectedMetaShipUid;
    /// <summary>The 장비 추천 card for the selected META fleet ship.</summary>
    public GearPanel MetaGearPanel => metaGearPanel;
    public string MetaFleetSummaryText => metaPlan is null
        ? ""
        : metaPlan.UsesGuide
            ? $"{metaPlan.Boss.Name} · 공략 {metaPlan.GuideSlotCount} · 차선 {metaPlan.AlternativeSlotCount}"
            : $"{metaPlan.Boss.Name} · 장갑 기준 자동 추천";
    public bool HasLastMetaSortie => metaFightLog.LatestFleetUids.Count > 0;
    public string MetaOverlapText => metaPlan is null ? ""
        : HasLastMetaSortie ? $"이 보스 마지막 출격과 {LastSortieOverlap()}/6척 같음"
        : $"게임 편성과 {metaPlan.GameFleetOverlap}/6척 같음";
    /// <summary>Every recommended ship is already in the compared fleet, so the chip reads as good rather than a warning.</summary>
    public bool IsMetaOverlapFull => metaPlan is not null && metaPlan.Slots.Any() &&
                                     (HasLastMetaSortie ? LastSortieOverlap() : metaPlan.GameFleetOverlap) == metaPlan.Slots.Count();
    public string MetaOverlapToolTip => HasLastMetaSortie
        ? "META 편성은 게임 기기에만 저장되고 보스마다 바꿔 쓰므로, 이 보스로 마지막에 캡처한 META전의 출격 함선과 비교했습니다"
        : "이 보스로 캡처한 META전이 없어 게임에 저장된 편성 중 가장 많이 겹치는 함대와 비교했습니다";
    public string MetaHintText => BuildMetaHint();
    public bool HasMetaHint => MetaHintText.Length > 0;

    public MetaBossChoice? SelectedBoss
    {
        get => selectedBoss;
        set
        {
            if (value is null || ReferenceEquals(value, selectedBoss)) return;
            selectedBoss = value;
            savedBossId = value.IsCurrent ? 0 : value.Id;
            OnPropertyChanged();
            RebuildMeta();
            settingsChanged();
        }
    }

    private void RebuildBossChoices()
    {
        var now = MetaBossCatalog.ServerTime(DateTimeOffset.Now);
        var bosses = plan.Inputs.MetaBosses;
        var current = MetaBossCatalog.Current(bosses, now);
        var choices = MetaBossCatalog.Selectable(bosses, now).Select((boss, index) =>
        {
            var isCurrent = boss.Id == current?.Id;
            var guide = plan.Inputs.MetaBossGuides.GetValueOrDefault(boss.Id);
            var startsGroup = index == 0 || (index == 1 && current is not null);
            return new MetaBossChoice(boss, isCurrent, guide is { Verified: true },
                boss.Id == plan.Inputs.GameArchiveBossId && !isCurrent,
                startsGroup ? isCurrent ? "현재 로테이션" : $"상시 보스 {bosses.Count(item => item.IsArchive)}종" : "",
                isCurrent ? boss.ActiveWindow(now)?.End : null);
        }).ToList();
        BossChoices.ReplaceRange(choices);
        var selection = MetaBossCatalog.DefaultSelection(bosses, now, savedBossId, plan.Inputs.GameArchiveBossId);
        selectedBoss = choices.FirstOrDefault(choice => choice.Id == selection?.Id);
        OnPropertyChanged(nameof(SelectedBoss));
    }

    private void RebuildMeta()
    {
        var boss = selectedBoss?.Boss;
        metaPlan = boss is null
            ? null
            : MetaBossPlanBuilder.Build(boss, plan.Inputs.MetaBossGuides.GetValueOrDefault(boss.Id), plan.Inputs.Ships,
                plan.Inputs.StarRules, MetaBossCatalog.ServerTime(DateTimeOffset.Now), plan.Inputs.GameFleets);
        // Keep the selected ship while it stays in the fleet; otherwise open the first stand-in, then the flagship.
        if (metaPlan is not null && metaPlan.Slots.All(slot => slot.ShipUid != selectedMetaShipUid))
        {
            selectedMetaShipUid = (metaPlan.Slots.FirstOrDefault(slot => slot.IsAlternative)
                                   ?? metaPlan.Slots.FirstOrDefault(slot => slot.IsFlagship)
                                   ?? metaPlan.Slots.FirstOrDefault())?.ShipUid ?? "";
        }

        OnPropertyChanged(nameof(MetaPlan));
        OnPropertyChanged(nameof(HasMetaPlan));
        OnPropertyChanged(nameof(HasNoMetaPlan));
        OnPropertyChanged(nameof(MetaFleetSummaryText));
        OnPropertyChanged(nameof(MetaHintText));
        OnPropertyChanged(nameof(HasMetaHint));
        RebuildGear();
        RebuildMetaFights();
    }

    private void RebuildGear()
    {
        metaGearPanel = GearPanel.Empty;
        var inputs = plan.Inputs;
        var selected = metaPlan?.Slots.FirstOrDefault(slot => slot.ShipUid == selectedMetaShipUid);
        if (metaPlan is not null && selected is not null && inputs.GearMap is not null)
        {
            // The fleet is allocated as a whole, flagship first, so the selected ship sees what its fleet mates keep.
            var fleet = metaPlan.MainSlots.Concat(metaPlan.VanguardSlots).OfType<MetaBossSlot>()
                .OrderByDescending(slot => slot.IsFlagship)
                .Where(slot => inputs.GearShips.ContainsKey(slot.ShipUid))
                .Select(slot => inputs.GearShips[slot.ShipUid] with { IsFlagship = slot.IsFlagship })
                .ToList();
            var alternate = metaPlan.Armors.Count > 1 ? MetaBossCatalog.GearUsecase([metaPlan.Armors[1]]) : "";
            var plans = GearAllocator.Allocate(fleet, inputs.GearMap, metaPlan.GearUsecase, inputs.GearInventory, gearMode,
                alternate);
            metaGearPanel = GearPanel.For(plans, selected.ShipUid, selected.Name, selected.SubText, selected.IconUrl,
                selected.Rarity, inputs, gearMode,
                metaPlan.Armors.Count > 1 ? MetaBossCatalog.ArmorLabel(metaPlan.Armors[0]) : "",
                metaPlan.Armors.Count > 1 ? MetaBossCatalog.ArmorLabel(metaPlan.Armors[1]) : "");
        }

        OnPropertyChanged(nameof(SelectedMetaShipUid));
        OnPropertyChanged(nameof(MetaGearPanel));
        OnPropertyChanged(nameof(IsGearMoveMode));
        OnPropertyChanged(nameof(IsGearBagMode));
    }

    private void SelectMetaShip(MetaBossSlot? slot)
    {
        if (slot is null || slot.ShipUid == selectedMetaShipUid) return;
        selectedMetaShipUid = slot.ShipUid;
        RebuildGear();
    }

    private void SetGearMode(string? mode)
    {
        var next = mode == "bag" ? GearSourceMode.Bag : GearSourceMode.Move;
        if (next == gearMode) return;
        gearMode = next;
        RebuildGear();
        RebuildLevelingGear();
        RebuildMetaFights();
        settingsChanged();
    }

    // Names the guide's first choices that are owned but not yet Lv.120, and where they wait in the leveling queue.
    private string BuildMetaHint()
    {
        if (metaPlan is not { UsesGuide: true }) return metaPlan is null ? "" : "이 보스는 검토된 공략이 없어 META 등급과 보스 장갑 기준 피해량으로만 골랐습니다.";
        var waiting = metaPlan.Slots.Where(slot => slot.IsAlternative && slot.Note.EndsWith(" 대신", StringComparison.Ordinal))
            .Select(slot => slot.Note[..^3].Split(" Lv.")[0])
            .ToList();
        if (waiting.Count == 0) return "";
        var queue = levelingPlan?.Queue ?? [];
        var queued = waiting
            .Select(name => (Name: name, Rank: queue.FirstOrDefault(row => row.Name == name)?.Rank))
            .OrderBy(item => item.Rank ?? int.MaxValue)
            .ToList();
        var names = string.Join("·", queued.Select(item => item.Name));
        return queued.All(item => item.Rank is not null)
            ? $"공략 1순위 {names}은(는) Lv.120 전이라 차선으로 채웠습니다. 쫄작 대기열 {string.Join("·", queued.Select(item => item.Rank))}순위에 있습니다."
            : $"공략 1순위 {names}은(는) Lv.120 전이라 차선으로 채웠습니다.";
    }
}

public sealed class MetaBossChoice(MetaBoss boss, bool isCurrent, bool hasGuide, bool isGameSelected, string groupLabel,
    DateTime? endsAt)
{
    public MetaBoss Boss { get; } = boss;
    public int Id => Boss.Id;
    public string Name => Boss.Name;
    public bool IsCurrent { get; } = isCurrent;
    public bool HasGuide { get; } = hasGuide;
    public bool IsGameSelected { get; } = isGameSelected;
    public string GroupLabel { get; } = groupLabel;
    public bool StartsGroup => GroupLabel.Length > 0;
    public string ArmorText => MetaBossCatalog.ArmorLabel(Boss.Armor);
    public string HullText => FleetPlanBuilder.HullLabel(Boss.Hull);
    public string GuideText => HasGuide ? "공략 있음" : "장갑 기준만";
    public string KindText => IsCurrent ? "현재 로테이션" : "상시";
    public string MarkText => IsCurrent && endsAt is { } end
        ? end.ToString("M/d까지", System.Globalization.CultureInfo.InvariantCulture)
        : IsGameSelected ? "게임 선택" : "";
    public bool HasMark => MarkText.Length > 0;
}
