namespace AzurLaneDashboard.Core.Models;

public sealed class SkillPriorityRow
{
    public int Score { get; init; }
    public string ShipUid { get; init; } = "";
    public string ShipName { get; init; } = "";
    public string ShipIconUrl { get; init; } = "";
    public string Rarity { get; init; } = "";
    public bool IsProposed { get; init; }
    public int ShipLevel { get; init; }
    public string ShipDisplayName => ShipName;
    public string ShipDisplayIconUrl => ShipIconUrl;
    public string ShipDisplaySummary => ShipLevel > 0 ? $"Lv.{ShipLevel:N0}" : "";
    public string SkillId { get; init; } = "";
    public string SkillName { get; init; } = "";
    public string SkillIconUrl { get; init; } = "";
    public int SkillLevel { get; init; }
    public int SkillMaxLevel { get; init; }
    public int SkillExp { get; init; }
    public string Reason { get; init; } = "";
}
