namespace AzurLaneDashboard.Data.Services;

public sealed class LivePacketStoreOptions
{
    public bool SavePacketPayloads { get; init; }
    public bool SaveDecodeFailurePayloads { get; init; }
    public int MaxPayloadBytes { get; init; } = 4096;
    public int RetentionSweepIntervalPackets { get; init; } = 500;
    public int SummaryWriteIntervalPackets { get; init; } = 100;
    public int MaxPacketPayloadRows { get; init; } = 5000;
    public int MaxDecodedPacketRows { get; init; } = 20000;
    public int MaxDecodeFailureRowsPerPacket { get; init; } = 3;
    public int MaxRecentDedupeKeys { get; init; } = 10000;
}
