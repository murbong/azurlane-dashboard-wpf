namespace AzurLaneDashboard.Core.Models;

public sealed class DashboardKpiRow
{
    public string Title { get; init; } = "";
    public string Value { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Kind { get; init; } = "";
}
