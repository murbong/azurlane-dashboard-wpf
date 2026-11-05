namespace AzurLaneDashboard.Core.Models;

public sealed class SystemStatusRow
{
    public string Name { get; init; } = "";
    public string StatusLabel { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Kind { get; init; } = "";

    // The normal live state is shown as a quiet dot; only exceptions earn a labelled badge.
    public bool IsRoutine => Kind == "good";
    public bool IsNotable => !IsRoutine && !string.IsNullOrWhiteSpace(StatusLabel);
}

public sealed class AcademyRoomRow
{
    public int RoomId { get; init; }
    public string ShipUid { get; init; } = "";
    public string ShipName { get; init; } = "";
    public string ShipIconUrl { get; init; } = "";
    public string SkillPosition { get; init; } = "";
    // skill_pos holds the skill ID; the name is what a player recognises.
    public string SkillName { get; init; } = "";
    public string FinishAt { get; init; } = "";
    public string Remaining { get; init; } = "";
    public int Exp { get; init; }
}

public sealed class ResearchQueueRow
{
    public int Order { get; init; }
    public string Id { get; init; } = "";
    public string FinishAt { get; init; } = "";
    public string Remaining { get; init; } = "";
}

public sealed class CommissionRow
{
    public string Id { get; init; } = "";
    public string FinishAt { get; init; } = "";
    public string Remaining { get; init; } = "";
    public string ShipNames { get; init; } = "";
}

public sealed class BuildQueueRow
{
    public string Slot { get; init; } = "";
    public string StatusLabel { get; init; } = "";
    public string StartedAt { get; init; } = "";
    public string FinishAt { get; init; } = "";
    public string Remaining { get; init; } = "";
    public string ResultName { get; init; } = "";
}

/// <summary>A resource the account holds, named and drawn as the item it stands for (maps/resources.json).</summary>
public sealed class ResourceRow
{
    public int Type { get; init; }
    public string Label { get; init; } = "";
    public string IconUrl { get; init; } = "";
    /// <summary>The icon frame's rarity on the ship/equipment scale (N … UR); items sit one step lower.</summary>
    public string Rarity { get; init; } = "";
    /// <summary>Gold, oil, gems, merit, coins and the like, listed first and kept at 0.</summary>
    public bool IsCore { get; init; }
    public int Count { get; init; }
    public string GroupLabel => IsCore ? "기본" : "이벤트·기타";
    public string Initial => Label.Length > 0 ? Label[..1] : "?";
}
