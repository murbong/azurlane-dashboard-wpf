using System.Globalization;

namespace AzurLaneDashboard.Core.Models;

/// <summary>A Meowfficer kind from maps/commanders.json (game data).</summary>
public sealed record MeowfficerKind(int Id, string Name, int Rarity, int SkillId, int MaxLevel, bool FixedTalents)
{
    public string RarityText => Rarity switch { 5 => "SSR", 4 => "SR", 3 => "R", _ => "" };
    /// <summary>Talent points a cat of this rarity can hold: SSR 10, SR 8, R 7.</summary>
    public int TalentCap => Rarity switch { 5 => 10, 4 => 8, _ => 7 };
}

/// <summary>A talent rank: talents of one line share a group, and worth is its talent points.</summary>
public sealed record MeowfficerTalent(int Id, string Name, int Group, int Worth, string Description);

public sealed record MeowfficerSkill(int Id, string Name, int Level, string Description);

public sealed record MeowfficerFleetType(string Key, string Label, IReadOnlyList<int> Core, IReadOnlyList<int> Filler,
    IReadOnlyList<int> Special);

/// <summary>Where a source places a cat: fleet type and slot, how strongly, and a summary in our own words.</summary>
public sealed record MeowfficerRole(string Fleet, string Slot, string Stance, string Summary, string Disputed);

public sealed record MeowfficerSource(string Title, string Url, string Author, string Revision, string License, string Role);

/// <summary>Hand-curated ratings from maps/meowfficer_guide.json.</summary>
public sealed class MeowfficerGuide
{
    public bool Verified { get; init; }
    public string LastReviewed { get; init; } = "";
    public IReadOnlyList<MeowfficerSource> Sources { get; init; } = [];
    public int UsablePoints { get; init; } = 2;
    public int GoodPoints { get; init; } = 3;
    public int GodPoints { get; init; } = 4;
    public int FlameGroup { get; init; }
    public IReadOnlyDictionary<string, MeowfficerFleetType> FleetTypes { get; init; } =
        new Dictionary<string, MeowfficerFleetType>();
    public IReadOnlyDictionary<int, MeowfficerRole> Roles { get; init; } = new Dictionary<int, MeowfficerRole>();
    public IReadOnlyDictionary<string, string> FleetNotes { get; init; } = new Dictionary<string, string>();
}

/// <summary>A cat the player owns, as the capture last saw it.</summary>
public sealed record OwnedMeowfficer(
    string Uid, int TemplateId, int Level, bool Locked, string Nickname, IReadOnlyList<int> Talents,
    IReadOnlyList<int> OriginTalents, int SkillId);

/// <summary>A sortie fleet: its ships' hull codes and the cats in its 지휘 (1) and 참모 (2) slots.</summary>
public sealed record MeowfficerFleetInput(int FleetId, IReadOnlyList<string> Hulls, string CommandUid, string StaffUid);

public enum MeowfficerVerdict
{
    /// <summary>Starting talents hold four points the source wants, with a special talent.</summary>
    God,
    Good,
    Usable,
    /// <summary>Too few wanted starting talents: the source would not invest, so it is material.</summary>
    Feed,
    Fixed,
    NoSource
}

public sealed class MeowfficerTalentChip
{
    public string Name { get; init; } = "";
    public int Group { get; init; }
    public int Worth { get; init; }
    /// <summary>"core", "filler", "off" (not wanted for this cat's fleet) or "fixed" (Tempesta).</summary>
    public string Kind { get; init; } = "";
    public string Description { get; init; } = "";

    public string KindText => Kind switch { "core" => "핵심", "filler" => "보조", "fixed" => "고정", _ => "이 함대에선 안 씀" };
    public string WorthText => $"{Worth}포인트";
    public string ToolTipText => $"{Name} · {WorthText} · {KindText}\n{Description}";
}

public sealed class MeowfficerCopyRow
{
    public string Uid { get; init; } = "";
    public int TemplateId { get; init; }
    public string Name { get; init; } = "";
    public string Nickname { get; init; } = "";
    public string Rarity { get; init; } = "";
    public int Level { get; init; }
    public int MaxLevel { get; init; }
    public bool Locked { get; init; }
    public bool Fixed { get; init; }
    public bool HasRole { get; init; }
    public string FleetLabel { get; init; } = "";
    public string Slot { get; init; } = "";
    public string RoleSummary { get; init; } = "";
    public string Disputed { get; init; } = "";
    public string SkillText { get; init; } = "";
    public IReadOnlyList<MeowfficerTalentChip> Talents { get; init; } = [];
    /// <summary>Points of wanted talents now; null for Tempesta cats, which the source rates as a whole.</summary>
    public int? Score { get; init; }
    public int Cap { get; init; }
    public MeowfficerVerdict Verdict { get; init; }
    public string NextTalent { get; init; } = "";
    public string ReplaceTalent { get; init; } = "";
    public IReadOnlyList<string> Assignments { get; init; } = [];

    public bool IsMaxLevel => MaxLevel > 0 && Level >= MaxLevel;
    public bool IsRaiseCandidate => !IsMaxLevel && Verdict is MeowfficerVerdict.God or MeowfficerVerdict.Good or MeowfficerVerdict.Usable;
    public string LevelText => $"Lv.{Level}";
    public string DisplayName => Nickname.Length > 0 ? $"{Name} ({Nickname})" : Name;
    public string SubText => string.Join(" · ", new[] { Locked ? "잠금" : "", Nickname }.Where(part => part.Length > 0));
    public string RoleText => HasRole ? $"{FleetLabel} {Slot}" : "출처 평가 없음";
    public string ScoreText => Score is { } score ? $"{score} / {Cap}" : "—";
    public string VerdictText => Verdict switch
    {
        MeowfficerVerdict.God => "최상 시작 재능",
        MeowfficerVerdict.Good => "좋은 시작 재능",
        MeowfficerVerdict.Usable => "쓸 만한 시작 재능",
        MeowfficerVerdict.Feed => "먹이 후보",
        MeowfficerVerdict.Fixed => "재능 고정 · 총평만",
        _ => "출처 평가 없음"
    };
    /// <summary>The chip at the row's end: talent points once the cat is fully raised, else the starting-talent call.</summary>
    public string ChipText => IsMaxLevel && Score is { } score ? $"재능 {score}/{Cap}" : VerdictText;
    /// <summary>"good", "warn", "muted" or "" for the chip colour; the text carries the meaning too.</summary>
    public string ChipKind => IsMaxLevel && Score is { } score
        ? score >= Cap * 0.7 ? "good" : score <= Cap * 0.3 ? "warn" : ""
        : Verdict switch
        {
            MeowfficerVerdict.God or MeowfficerVerdict.Good => "good",
            MeowfficerVerdict.Feed => "warn",
            MeowfficerVerdict.Fixed or MeowfficerVerdict.NoSource => "muted",
            _ => ""
        };
    public string AssignmentText => string.Join(", ", Assignments);
    public bool HasAssignments => Assignments.Count > 0;
    public bool HasNextTalent => NextTalent.Length > 0;
    public bool HasReplaceTalent => ReplaceTalent.Length > 0;
    public bool HasDisputed => Disputed.Length > 0;
    public string BriefText => $"{DisplayName} Lv.{Level}" + (Score is { } value ? $" · 재능 {value}/{Cap}" : "");
}

public sealed class MeowfficerKindRow
{
    public int TemplateId { get; init; }
    public string Name { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string RoleText { get; init; } = "";
    public bool HasRole { get; init; }
    public IReadOnlyList<MeowfficerCopyRow> Copies { get; init; } = [];
}

public sealed class MeowfficerSlotRow
{
    public string Slot { get; init; } = "";
    public MeowfficerCopyRow? Current { get; init; }
    public string Status { get; init; } = "";
    /// <summary>"good", "info", "warn" or "muted".</summary>
    public string StatusKind { get; init; } = "";
    public string Note { get; init; } = "";
    public IReadOnlyList<MeowfficerCopyRow> Candidates { get; init; } = [];

    public string CurrentText => Current?.BriefText ?? "없음";
    public bool HasCandidates => Candidates.Count > 0;
    public string CandidateText => Candidates.Count == 0 ? "" : "후보 " + string.Join(" / ", Candidates.Select(candidate =>
        candidate.BriefText + (candidate.HasAssignments ? $" · {candidate.AssignmentText} 사용 중" : "")));
    public string CandidateSummary => Candidates.FirstOrDefault()?.RoleSummary ?? "";
    public bool HasNote => Note.Length > 0;
}

public sealed class MeowfficerFleetRow
{
    public int FleetId { get; init; }
    public string TypeLabel { get; init; } = "";
    public string Purpose { get; init; } = "";
    public string HullText { get; init; } = "";
    public IReadOnlyList<MeowfficerSlotRow> Slots { get; init; } = [];

    public string Title => $"{FleetId.ToString(CultureInfo.InvariantCulture)}함대";
    public string TypeText => $"{TypeLabel} 함대" + (Purpose.Length > 0 ? $" · {Purpose}" : "");
}

public sealed class MeowfficerDashboard
{
    public static MeowfficerDashboard Empty { get; } = new();

    public IReadOnlyList<MeowfficerFleetRow> Fleets { get; init; } = [];
    public IReadOnlyList<MeowfficerKindRow> Kinds { get; init; } = [];
    public IReadOnlyList<MeowfficerSource> Sources { get; init; } = [];
    public bool UsesGuide { get; init; }

    public IEnumerable<MeowfficerCopyRow> Copies => Kinds.SelectMany(kind => kind.Copies);
    public int Count => Copies.Count();
    public bool HasCats => Kinds.Count > 0;
    public string PrimarySourceText => Sources.FirstOrDefault(source => source.Role == "primary") is { } source
        ? $"{source.Title} · {source.Revision} · {source.License}"
        : "";
    public string SecondarySourceText => Sources.FirstOrDefault(source => source.Role == "secondary") is { } source
        ? $"{source.Title} · {source.Revision}"
        : "";
}
