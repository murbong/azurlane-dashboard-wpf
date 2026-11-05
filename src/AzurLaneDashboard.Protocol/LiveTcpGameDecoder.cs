namespace AzurLaneDashboard.Protocol;

public sealed class LiveTcpGameDecoder(int serverPort, IEnumerable<string>? hostFilter, string source)
{
    private const int MaxTrackedStreams = 256;
    private readonly HashSet<string> hostFilter = new(hostFilter?.Where(host => !string.IsNullOrWhiteSpace(host)) ?? [], StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Src, int Sport, string Dst, int Dport), StreamState> streams = new();

    public IEnumerable<GamePacket> FeedFrame(PcapFrame frame)
    {
        var segment = PcapStreamReader.ParseTcpSegment(frame.Timestamp, frame.LinkType, frame.Data);
        if (segment is null)
        {
            return [];
        }

        if (hostFilter.Count > 0 && !hostFilter.Contains(segment.SourceAddress) && !hostFilter.Contains(segment.DestinationAddress))
        {
            return [];
        }

        if (serverPort > 0 && segment.SourcePort != serverPort && segment.DestinationPort != serverPort)
        {
            return [];
        }

        var key = (segment.SourceAddress, segment.SourcePort, segment.DestinationAddress, segment.DestinationPort);
        if (!streams.TryGetValue(key, out var state))
        {
            if (streams.Count >= MaxTrackedStreams)
            {
                var oldest = streams.MinBy(pair => pair.Value.LastSeen).Key;
                streams.Remove(oldest);
            }

            state = new StreamState(
                new GameFrameStream(),
                $"{segment.SourceAddress}:{segment.SourcePort}-{segment.DestinationAddress}:{segment.DestinationPort}",
                segment.Timestamp);
            streams[key] = state;
        }

        state.LastSeen = segment.Timestamp;
        var frames = state.Stream.Feed(segment);
        if (frames.Count == 0)
        {
            return [];
        }

        var packets = new GamePacket[frames.Count];
        for (var index = 0; index < frames.Count; index++)
        {
            var frameInfo = frames[index];
            packets[index] = new GamePacket(
                source,
                state.StreamId,
                Direction(frameInfo.PacketId, segment),
                frameInfo.PacketId,
                frameInfo.Index,
                frameInfo.Payload);
        }

        return packets;
    }

    private string Direction(int packetId, TcpSegment segment)
    {
        if (serverPort > 0)
        {
            return segment.DestinationPort == serverPort ? "CS" : "SC";
        }

        return PacketIds.ClientPacketIds.Contains(packetId) ? "CS" : "SC";
    }

    private sealed class StreamState(GameFrameStream stream, string streamId, double lastSeen)
    {
        public GameFrameStream Stream { get; } = stream;
        public string StreamId { get; } = streamId;
        public double LastSeen { get; set; } = lastSeen;
    }
}
