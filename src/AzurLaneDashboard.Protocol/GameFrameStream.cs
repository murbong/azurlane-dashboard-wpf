using System.Runtime.InteropServices;

namespace AzurLaneDashboard.Protocol;

public sealed class GameFrameStream
{
    private const int MaxStreamBuffer = 4 * 1024 * 1024;
    private readonly List<byte> buffer = [];
    private uint? expectedSequence;

    public IReadOnlyList<(int PacketId, int Index, byte[] Payload)> Feed(TcpSegment segment)
    {
        var data = segment.Payload;
        if (expectedSequence is null)
        {
            buffer.AddRange(data);
            expectedSequence = segment.Sequence + (uint)data.Length;
            return PopFrames();
        }

        if (segment.Sequence == expectedSequence)
        {
            buffer.AddRange(data);
            expectedSequence += (uint)data.Length;
            return PopFrames();
        }

        if (AfterOrEqual(expectedSequence.Value, segment.Sequence))
        {
            var overlap = unchecked(expectedSequence.Value - segment.Sequence);
            if (overlap >= data.Length)
            {
                return [];
            }

            buffer.AddRange(data.AsSpan((int)overlap));
            expectedSequence += (uint)(data.Length - overlap);
            return PopFrames();
        }

        buffer.Clear();
        buffer.AddRange(data);
        expectedSequence = segment.Sequence + (uint)data.Length;
        return PopFrames();
    }

    private List<(int PacketId, int Index, byte[] Payload)> PopFrames()
    {
        var frames = new List<(int PacketId, int Index, byte[] Payload)>();
        var data = CollectionsMarshal.AsSpan(buffer);
        var pos = 0;
        while (pos + 7 <= data.Length)
        {
            if (!LooksLikeFrame(data, pos))
            {
                pos++;
                continue;
            }

            var packetSize = (data[pos] << 8) | data[pos + 1];
            var frameSize = packetSize + 2;
            if (pos + frameSize > data.Length)
            {
                break;
            }

            var packetId = (data[pos + 3] << 8) | data[pos + 4];
            var index = (data[pos + 5] << 8) | data[pos + 6];
            var payload = data.Slice(pos + 7, frameSize - 7).ToArray();
            frames.Add((packetId, index, payload));
            pos += frameSize;
        }

        if (pos > 0)
        {
            buffer.RemoveRange(0, pos);
        }

        if (buffer.Count > MaxStreamBuffer)
        {
            buffer.RemoveRange(0, buffer.Count - 64);
        }

        return frames;
    }

    private static bool LooksLikeFrame(ReadOnlySpan<byte> data, int pos)
    {
        var packetSize = (data[pos] << 8) | data[pos + 1];
        var frameSize = packetSize + 2;
        if (packetSize < 5 || frameSize < 7 || data[pos + 2] != 0)
        {
            return false;
        }

        var packetId = (data[pos + 3] << 8) | data[pos + 4];
        return packetId is >= 10000 and <= 70000;
    }

    private static bool AfterOrEqual(uint a, uint b) => unchecked(a - b) < 0x80000000u;
}
