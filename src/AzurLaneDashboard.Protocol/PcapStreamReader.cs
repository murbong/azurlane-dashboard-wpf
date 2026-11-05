using System.Buffers.Binary;
using System.Net;

namespace AzurLaneDashboard.Protocol;


public sealed class PcapStreamReader(Stream stream)
{
    private const int LinkTypeEthernet = 1;
    private const int LinkTypeRaw = 101;
    private const int LinkTypeLinuxSll = 113;
    private const int LinkTypeLinuxSll2 = 276;

    private int linkType;
    private bool littleEndian;
    private double divisor;
    private bool initialized;

    public async IAsyncEnumerable<PcapFrame> ReadFramesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var header = await ReadExactAsync(16, false, cancellationToken).ConfigureAwait(false);
            if (header.Length == 0)
            {
                yield break;
            }

            if (header.Length != 16)
            {
                throw new PcapException("truncated pcap packet header");
            }

            var tsSec = ReadUInt32(header.AsSpan(0, 4));
            var tsFrac = ReadUInt32(header.AsSpan(4, 4));
            var inclLen = checked((int)ReadUInt32(header.AsSpan(8, 4)));
            var data = await ReadExactAsync(inclLen, true, cancellationToken).ConfigureAwait(false);
            yield return new PcapFrame(tsSec + (tsFrac / divisor), linkType, data);
        }
    }

    public static TcpSegment? ParseTcpSegment(double timestamp, int linkType, byte[] frame)
    {
        var extracted = ExtractIpPacket(linkType, frame);
        if (extracted is null)
        {
            return null;
        }

        var (ethertype, packet) = extracted.Value;
        byte[] ipPayload;
        string source;
        string destination;
        if (ethertype == 0x0800)
        {
            if (packet.Length < 20 || packet[0] >> 4 != 4)
            {
                return null;
            }

            var ihl = (packet[0] & 0x0F) * 4;
            if (ihl < 20 || packet.Length < ihl)
            {
                return null;
            }

            var totalLength = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(2, 2));
            var protocol = packet[9];
            var fragment = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6, 2));
            if (protocol != 6 || (fragment & 0x3FFF) != 0)
            {
                return null;
            }

            ipPayload = totalLength > 0 && totalLength <= packet.Length
                ? packet[ihl..totalLength]
                : packet[ihl..];
            source = new IPAddress(packet.AsSpan(12, 4)).ToString();
            destination = new IPAddress(packet.AsSpan(16, 4)).ToString();
        }
        else if (ethertype == 0x86DD)
        {
            if (packet.Length < 40 || packet[0] >> 4 != 6 || packet[6] != 6)
            {
                return null;
            }

            var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4, 2));
            ipPayload = payloadLength > 0 && 40 + payloadLength <= packet.Length
                ? packet[40..(40 + payloadLength)]
                : packet[40..];
            source = new IPAddress(packet.AsSpan(8, 16)).ToString();
            destination = new IPAddress(packet.AsSpan(24, 16)).ToString();
        }
        else
        {
            return null;
        }

        if (ipPayload.Length < 20)
        {
            return null;
        }

        var sourcePort = BinaryPrimitives.ReadUInt16BigEndian(ipPayload.AsSpan(0, 2));
        var destinationPort = BinaryPrimitives.ReadUInt16BigEndian(ipPayload.AsSpan(2, 2));
        var sequence = BinaryPrimitives.ReadUInt32BigEndian(ipPayload.AsSpan(4, 4));
        var dataOffset = (ipPayload[12] >> 4) * 4;
        if (dataOffset < 20 || ipPayload.Length < dataOffset)
        {
            return null;
        }

        var payload = ipPayload[dataOffset..];
        return payload.Length == 0
            ? null
            : new TcpSegment(timestamp, source, sourcePort, destination, destinationPort, sequence, payload);
    }

    private static (int Ethertype, byte[] Packet)? ExtractIpPacket(int linkType, byte[] frame)
    {
        if (linkType == LinkTypeEthernet)
        {
            if (frame.Length < 14)
            {
                return null;
            }

            var offset = 14;
            var ethertype = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(12, 2));
            while ((ethertype == 0x8100 || ethertype == 0x88A8) && frame.Length >= offset + 4)
            {
                ethertype = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(offset + 2, 2));
                offset += 4;
            }

            return (ethertype, frame[offset..]);
        }

        if (linkType == LinkTypeLinuxSll)
        {
            return frame.Length < 16
                ? null
                : (BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(14, 2)), frame[16..]);
        }

        if (linkType == LinkTypeLinuxSll2)
        {
            return frame.Length < 20
                ? null
                : (BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(0, 2)), frame[20..]);
        }

        if (linkType == LinkTypeRaw)
        {
            if (frame.Length == 0)
            {
                return null;
            }

            return (frame[0] >> 4) switch
            {
                4 => (0x0800, frame),
                6 => (0x86DD, frame),
                _ => null
            };
        }

        return null;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
        {
            return;
        }

        var magic = await ReadExactAsync(4, false, cancellationToken).ConfigureAwait(false);
        if (magic.Length == 0)
        {
            throw new PcapException("empty pcap stream");
        }

        (littleEndian, divisor) = magic switch
        {
            [0xD4, 0xC3, 0xB2, 0xA1] => (true, 1_000_000d),
            [0xA1, 0xB2, 0xC3, 0xD4] => (false, 1_000_000d),
            [0x4D, 0x3C, 0xB2, 0xA1] => (true, 1_000_000_000d),
            [0xA1, 0xB2, 0x3C, 0x4D] => (false, 1_000_000_000d),
            _ => throw new PcapException("only classic pcap streams are supported")
        };

        var header = await ReadExactAsync(20, true, cancellationToken).ConfigureAwait(false);
        linkType = checked((int)ReadUInt32(header.AsSpan(16, 4)));
        initialized = true;
    }

    private uint ReadUInt32(ReadOnlySpan<byte> value)
    {
        return littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(value)
            : BinaryPrimitives.ReadUInt32BigEndian(value);
    }

    private async Task<byte[]> ReadExactAsync(int size, bool requireFull, CancellationToken cancellationToken)
    {
        var buffer = new byte[size];
        var offset = 0;
        while (offset < size)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, size - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (offset == 0 && !requireFull)
                {
                    return [];
                }

                throw new PcapException("truncated pcap stream");
            }

            offset += read;
        }

        return buffer;
    }
}
