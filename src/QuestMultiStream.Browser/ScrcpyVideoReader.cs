using System.Buffers.Binary;

namespace QuestMultiStream.Browser;

// scrcpy 3.3.4 app/src/demuxer.c: big-endian codec/size and packet metadata.
public sealed class ScrcpyVideoReader(Stream stream)
{
    public const int MaximumPacketBytes = 16 * 1024 * 1024;
    public async Task<(int Width, int Height)> Header(CancellationToken ct, bool dummyAlreadyRead = false)
    {
        var hello = new byte[dummyAlreadyRead ? 64 : 65]; await stream.ReadExactlyAsync(hello, ct);
        if (!dummyAlreadyRead && hello[0] != 0) throw new InvalidDataException("Bad forward connection dummy byte");
        var codec = new byte[12]; await stream.ReadExactlyAsync(codec, ct);
        if (BinaryPrimitives.ReadUInt32BigEndian(codec) != 0x68323634) throw new InvalidDataException("Only selected H.264 is supported");
        var width = BinaryPrimitives.ReadInt32BigEndian(codec.AsSpan(4));
        var height = BinaryPrimitives.ReadInt32BigEndian(codec.AsSpan(8));
        if (width is < 1 or > 16384 || height is < 1 or > 16384) throw new InvalidDataException("Invalid video dimensions");
        return (width, height);
    }
    public async Task<byte[]?> Packet(CancellationToken ct)
    {
        var header = new byte[12];
        var first = await stream.ReadAsync(header.AsMemory(0, 1), ct);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), ct);
        var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
        if (length is 0 or > MaximumPacketBytes) throw new InvalidDataException("Invalid packet size");
        var packet = new byte[12 + length]; header.CopyTo(packet, 0);
        await stream.ReadExactlyAsync(packet.AsMemory(12), ct);
        return packet;
    }
}
