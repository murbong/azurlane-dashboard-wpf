namespace AzurLaneDashboard.Protocol;

public sealed record GamePacket(
    string Source,
    string StreamId,
    string Direction,
    int PacketId,
    int Index,
    byte[] Payload);

public sealed record DecodedGamePacket(GamePacket Packet, string Json);

public sealed record PcapFrame(double Timestamp, int LinkType, byte[] Data);

public sealed record TcpSegment(
    double Timestamp,
    string SourceAddress,
    int SourcePort,
    string DestinationAddress,
    int DestinationPort,
    uint Sequence,
    byte[] Payload);

