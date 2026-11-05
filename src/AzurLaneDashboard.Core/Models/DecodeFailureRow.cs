namespace AzurLaneDashboard.Core.Models;

public sealed class DecodeFailureRow
{
    public string PacketId { get; init; } = "";
    public string PacketName { get; init; } = "";
    public string Reason { get; init; } = "";
    public string Error { get; init; } = "";
    public int Count { get; init; }
}
