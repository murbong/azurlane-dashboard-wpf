using System.Globalization;
using AzurLaneDashboard.Core.Services;

namespace AzurLaneDashboard.Core.Models;

public sealed record MetaBossWindow(DateTime Start, DateTime End)
{
    public bool Contains(DateTime serverTime) => serverTime >= Start && serverTime <= End;
}

public sealed record MetaBossLevel(int Level, int Hp, int EnemyId, int ExpeditionId);

public sealed record MetaBossSkill(string Name, int Grade, string Text);

/// <summary>A META Showdown boss from maps/meta_bosses.json (game data only).</summary>
public sealed record MetaBoss
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int MetaId { get; init; }
    /// <summary>"always" (archive), "stop" (hidden) or "timed" (rotation windows).</summary>
    public string Availability { get; init; } = "";
    public IReadOnlyList<MetaBossWindow> Windows { get; init; } = [];
    /// <summary>"Light", "Medium" or "Heavy" (Regal's armor names).</summary>
    public string Armor { get; init; } = "";
    public string Hull { get; init; } = "";
    public int BattleSeconds { get; init; }
    public IReadOnlyList<MetaBossLevel> Levels { get; init; } = [];
    public IReadOnlyList<MetaBossSkill> Skills { get; init; } = [];

    public bool IsArchive => Availability == "always";
    public bool IsActive(DateTime serverTime) => Availability == "timed" && Windows.Any(window => window.Contains(serverTime));
    public MetaBossWindow? ActiveWindow(DateTime serverTime) => Windows.FirstOrDefault(window => window.Contains(serverTime));
    public int TopLevel => Levels.Count == 0 ? 0 : Levels.Max(level => level.Level);
    public int TopHp => Levels.OrderByDescending(level => level.Level).Select(level => level.Hp).FirstOrDefault();
}

public sealed record MetaBossMechanic(string Title, string Text, string Response = "");

public sealed record MetaBossSource(string Title, string Url, string SourceUpdated);

/// <summary>
/// Hand-curated strategy for one boss (maps/meta_boss_guides.json). Each fleet slot lists the guide's
/// choices in order; an entry is used only once someone has checked it against the source.
/// </summary>
public sealed record MetaBossGuide
{
    public int BossId { get; init; }
    public bool Verified { get; init; }
    public string LastReviewed { get; init; } = "";
    /// <summary>Armors the boss switches between, e.g. Medium and Heavy every 25 seconds.</summary>
    public IReadOnlyList<string> ArmorAlternates { get; init; } = [];
    public int ArmorPeriodSeconds { get; init; }
    public IReadOnlyList<MetaBossMechanic> Mechanics { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<string>> Main { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<string>> Vanguard { get; init; } = [];
    public int FlagshipIndex { get; init; }
    public string Notes { get; init; } = "";
    public IReadOnlyList<MetaBossSource> Sources { get; init; } = [];
}

/// <summary>A Regal asterisk rating that holds only against bosses of these hulls or armors.</summary>
public sealed record RegalStarRule(string RegalUid, IReadOnlyList<string> Hulls, IReadOnlyList<string> Armors)
{
    public bool Applies(MetaBoss boss, IReadOnlyCollection<string> armors) =>
        Hulls.Contains(boss.Hull, StringComparer.OrdinalIgnoreCase) ||
        Armors.Any(armor => armors.Contains(armor, StringComparer.OrdinalIgnoreCase));
}

public enum MetaBossSlotSource
{
    /// <summary>The guide's first choice.</summary>
    Guide,
    /// <summary>A later guide choice because the first is missing or under Lv.120.</summary>
    GuideAlternative,
    /// <summary>Picked by rating and armor damage because the guide has nothing usable.</summary>
    Auto
}

public sealed class MetaBossSlot
{
    public FleetPlannerShip Ship { get; init; } = new();
    public MetaBossSlotSource Source { get; init; }
    public string Rating { get; init; } = "";
    public bool IsFlagship { get; init; }
    /// <summary>"★ 기함 · 기함 필요도 3/5" when Regal's flag requirement placed it; empty for a guide's flagship.</summary>
    public string FlagshipText { get; init; } = "";
    /// <summary>The first vanguard, which the battle code makes the fleet's leader.</summary>
    public bool IsLead { get; init; }
    /// <summary>Why the lead stands there: the guide, or Regal's survivability when the guide left it open.</summary>
    public string LeadText { get; init; } = "";
    /// <summary>Why this ship stands in, e.g. "아즈치 Lv.103 대신".</summary>
    public string Note { get; init; } = "";

    public string ShipUid => Ship.Uid;
    public string Name => Ship.Name;
    public string IconUrl => Ship.IconUrl;
    public string Rarity => Ship.Rarity;
    public string RatingTier => FleetPlanRating.Tier(Rating);
    public bool IsAlternative => Source == MetaBossSlotSource.GuideAlternative;
    public bool IsAuto => Source == MetaBossSlotSource.Auto;
    /// <summary>Third line under the portrait: why the ship stands here.</summary>
    public string ToolTipText => Note.Length > 0 ? $"{Ship.Name} · {Note}" : Ship.Name;
    public string RoleText => Note.Length > 0 ? System.Text.RegularExpressions.Regex.Replace(Note, @" Lv\.\d+", "")
        : IsFlagship ? FlagshipText.Length > 0 ? FlagshipText : "★ 기함"
        : IsLead ? LeadText
        : Source == MetaBossSlotSource.Guide ? "공략 1순위"
        : "";
    public string SourceText => Source switch
    {
        MetaBossSlotSource.Guide => "공략",
        MetaBossSlotSource.GuideAlternative => "차선",
        _ => "자동"
    };
    public string SubText => $"Lv.{Ship.Level} · {FleetPlanBuilder.HullLabel(Ship.Hull)}";
}

public sealed class MetaBossPlan
{
    public MetaBoss Boss { get; init; } = new();
    public bool UsesGuide { get; init; }
    public MetaBossGuide? Guide { get; init; }
    public IReadOnlyList<string> Armors { get; init; } = [];
    public IReadOnlyList<string> DamageMetrics { get; init; } = [];
    public string GearUsecase { get; init; } = "";
    public IReadOnlyList<MetaBossMechanic> Mechanics { get; init; } = [];
    public IReadOnlyList<MetaBossSlot?> MainSlots { get; init; } = [];
    public IReadOnlyList<MetaBossSlot?> VanguardSlots { get; init; } = [];
    public bool IsCurrent { get; init; }
    public DateTime? EndsAt { get; init; }
    public int GameFleetOverlap { get; init; }

    public IEnumerable<MetaBossSlot> Slots => MainSlots.Concat(VanguardSlots).OfType<MetaBossSlot>();
    public string ArmorText => Armors.Count > 1
        ? string.Join(" ↔ ", Armors.Select(MetaBossCatalog.ArmorLabel)) +
          (Guide?.ArmorPeriodSeconds > 0 ? $" · {Guide.ArmorPeriodSeconds}초마다 교대" : "")
        : MetaBossCatalog.ArmorLabel(Boss.Armor);
    public string HullText => FleetPlanBuilder.HullLabel(Boss.Hull);
    public string HpText => string.Create(CultureInfo.CurrentCulture, $"{Boss.TopHp:N0} (Lv.{Boss.TopLevel})");
    public string BattleTimeText => Boss.BattleSeconds > 0 ? $"{Boss.BattleSeconds}초" : "-";
    public string AvailabilityText => IsCurrent && EndsAt is { } end
        ? end.ToString("M/d HH:mm까지", CultureInfo.InvariantCulture)
        : Boss.IsArchive ? "상시" : "";
    public string SourceText => UsesGuide && Guide is not null
        ? "출처 · " + string.Join(" · ", Guide.Sources.Select(source => $"{source.Title} · 원문 {source.SourceUpdated}")) +
          $" · 앱 검토 {Guide.LastReviewed}"
        : "공략 미검토 · 기믹은 게임 스킬 설명, 편성은 장갑 기준 자동 추천";
    public string GearBasisText => Armors.Count > 1
        ? $"{MetaBossCatalog.ArmorLabel(Armors[0])} · {MetaBossCatalog.ArmorLabel(Armors[1])} 순위가 다르면 칸마다 표시"
        : MetaBossCatalog.ArmorLabel(Armors.FirstOrDefault() ?? "");
    public bool HasAvailabilityChip => IsCurrent && EndsAt is not null;
    public int GuideSlotCount => Slots.Count(slot => slot.Source == MetaBossSlotSource.Guide);
    public int AlternativeSlotCount => Slots.Count(slot => slot.Source == MetaBossSlotSource.GuideAlternative);
}
