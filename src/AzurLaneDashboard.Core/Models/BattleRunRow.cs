namespace AzurLaneDashboard.Core.Models;

public sealed class BattleRunRow
{
    public string StageId { get; init; } = "";
    public string StageLabel { get; init; } = "";
    public string StageDetail { get; init; } = "";
    public string SystemLabel { get; init; } = "";
    public int TotalTime { get; init; }
    public int ShipExpAvg { get; init; }
    public int ShipExpTotal { get; init; }
    public string MvpName { get; init; } = "";
    public string TopExpSummary { get; init; } = "";
    public bool IsBoss { get; init; }
}
