namespace AzurLaneDashboard.Core.Models;

public sealed class PacketStatRow
{
    public string PacketId { get; init; } = "";
    public string PacketName { get; init; } = "";
    public string DecodeStatus { get; init; } = "";
    public string Description { get; init; } = "";
    public int Count { get; init; }
}
