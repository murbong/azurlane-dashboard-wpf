namespace AzurLaneDashboard.Core.Models;

/// <summary>One step of a character task chain from maps/personal_tasks.json.</summary>
public sealed record PersonalTaskStep(int Id, string Name, string Desc, int Target);

/// <summary>A chain started by touching the ship (group) as secretary; group 0 is started by an activity.</summary>
public sealed record PersonalTaskChain(int Group, IReadOnlyList<PersonalTaskStep> Steps);

/// <summary>A task in the player's list: progress, and when it was accepted and submitted (0 = not yet).</summary>
public sealed record PlayerTask(int Id, int Progress, long AcceptTime, long SubmitTime);

/// <summary>A ship group as the page shows it.</summary>
public sealed record PersonalTaskShip(string Name, string IconUrl, string Rarity, int Level);

public static class PersonalTaskStatus
{
    public const string Done = "done";
    public const string Active = "active";
    public const string Ready = "ready";
    public const string Locked = "locked";
}

public sealed class PersonalTaskRow
{
    public int Group { get; init; }
    public string ShipName { get; init; } = "";
    public string IconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public bool IsOwned { get; init; }
    public int Level { get; init; }
    /// <summary>One of <see cref="PersonalTaskStatus"/>.</summary>
    public string Status { get; init; } = PersonalTaskStatus.Locked;
    public int DoneSteps { get; init; }
    public int StepCount { get; init; }
    public string StepName { get; init; } = "";
    public string StepDesc { get; init; } = "";
    public int Progress { get; init; }
    public int Target { get; init; }

    public string StepText => $"{DoneSteps}/{StepCount}단계";
    public double Fraction => Status == PersonalTaskStatus.Done ? 1
        : Target <= 0 ? 0 : Math.Min(1, (double)Progress / Target);
    /// <summary>Affection targets are stored ×100 (10000 = 100).</summary>
    public string ProgressText => Target == 10000
        ? $"호감도 {Progress / 100}/100"
        : $"{Math.Min(Progress, Target)}/{Target}";
    public bool HasProgress => Status == PersonalTaskStatus.Active;
    public string StatusText => Status switch
    {
        PersonalTaskStatus.Done => "완료",
        PersonalTaskStatus.Active => "진행 중",
        PersonalTaskStatus.Ready => "시작 가능",
        _ => "미보유"
    };
    /// <summary>The chip colour: in = done, next = active, out = ready to start, wait = not owned.</summary>
    public string StatusKind => Status switch
    {
        PersonalTaskStatus.Done => "in",
        PersonalTaskStatus.Active => "next",
        PersonalTaskStatus.Ready => "out",
        _ => "wait"
    };
    public string SubText => Status switch
    {
        PersonalTaskStatus.Done => $"{StepCount}단계 모두 완료",
        PersonalTaskStatus.Ready => $"Lv.{Level} · 비서함으로 두고 메인 화면에서 터치하면 시작",
        PersonalTaskStatus.Locked => "함선을 얻으면 시작할 수 있습니다",
        _ => StepName
    };
}

public sealed class PersonalTaskDashboard
{
    public static PersonalTaskDashboard Empty { get; } = new();

    public bool HasData { get; init; }
    public string BasisText { get; init; } = "";
    public IReadOnlyList<PersonalTaskRow> Rows { get; init; } = [];

    public int DoneCount => Rows.Count(row => row.Status == PersonalTaskStatus.Done);
    public int ActiveCount => Rows.Count(row => row.Status == PersonalTaskStatus.Active);
    public int ReadyCount => Rows.Count(row => row.Status == PersonalTaskStatus.Ready);
    public int LockedCount => Rows.Count(row => row.Status == PersonalTaskStatus.Locked);
}
