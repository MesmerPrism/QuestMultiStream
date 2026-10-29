using System.Buffers.Binary;

namespace QuestMultiStream.Browser;

// scrcpy 3.3.4 app/src/demuxer.c: big-endian codec/size and packet metadata.
public sealed class ScrcpyVideoReader(Stream stream, ScrcpyReadProgress? progress = null)
{
    public const int MaximumPacketBytes = 16 * 1024 * 1024;
    public ScrcpyReadProgress Progress { get; } = progress ?? new();
    public async Task<(int Width, int Height)> Header(CancellationToken ct, bool dummyAlreadyRead = false)
    {
        try
        {
            var hello = new byte[dummyAlreadyRead ? 64 : 65];
            Progress.Begin("device-name", hello.Length); await ReadExactly(hello, ct);
            if (!dummyAlreadyRead && hello[0] != 0) throw new InvalidDataException("Bad forward connection dummy byte");
            var codec = new byte[12]; Progress.Begin("metadata", codec.Length); await ReadExactly(codec, ct);
            if (BinaryPrimitives.ReadUInt32BigEndian(codec) != 0x68323634) throw new InvalidDataException("Only selected H.264 is supported");
            var width = BinaryPrimitives.ReadInt32BigEndian(codec.AsSpan(4));
            var height = BinaryPrimitives.ReadInt32BigEndian(codec.AsSpan(8));
            if (width is < 1 or > 16384 || height is < 1 or > 16384) throw new InvalidDataException("Invalid video dimensions");
            Progress.MetadataReady(); return (width, height);
        }
        catch (Exception e) { Progress.End(e is OperationCanceledException ? "cancelled" : "failed"); throw; }
    }
    public async Task<byte[]?> Packet(CancellationToken ct)
    {
        try
        {
            var header = new byte[12]; Progress.Begin("packet-header", header.Length);
            var first = await Read(header.AsMemory(0, 1), ct);
            if (first == 0) { Progress.End("eof"); return null; }
            await ReadExactly(header.AsMemory(1), ct);
            var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
            if (length is 0 or > MaximumPacketBytes) throw new InvalidDataException("Invalid packet size");
            var packet = new byte[12 + length]; header.CopyTo(packet, 0);
            Progress.Begin("packet-body", (int)length); await ReadExactly(packet.AsMemory(12), ct);
            Progress.PacketReady(BinaryPrimitives.ReadUInt64BigEndian(header)); return packet;
        }
        catch (Exception e) { Progress.End(e is OperationCanceledException ? "cancelled" : "failed"); throw; }
    }
    async ValueTask<int> Read(Memory<byte> buffer, CancellationToken ct)
    {
        var count = await stream.ReadAsync(buffer, ct); Progress.Consumed(count); return count;
    }
    async Task ReadExactly(Memory<byte> buffer, CancellationToken ct)
    {
        while (!buffer.IsEmpty)
        {
            var count = await Read(buffer, ct);
            if (count == 0) throw new EndOfStreamException();
            buffer = buffer[count..];
        }
    }
}

// Constant-size host observations only: no packet bytes, transport authority,
// native-clock comparison, retries, or completion/retirement claims.
public sealed class ScrcpyReadProgress
{
    readonly object sync = new();
    string stage = "startup", outcome = "idle";
    int stageBytes, stageExpectedBytes;
    long wireBytes, configPackets, keyPackets, deltaPackets;
    bool metadataComplete;
    DateTimeOffset? firstPacketUtc, lastPacketUtc;
    ulong? lastPts;
    public ScrcpyReadSnapshot Snapshot()
    {
        lock (sync) return new(stage, outcome, stageBytes, stageExpectedBytes, wireBytes,
            metadataComplete, configPackets, keyPackets, deltaPackets, firstPacketUtc, lastPacketUtc,
            lastPts?.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    internal void Begin(string value, int expected)
    {
        lock (sync) { stage = value; stageBytes = 0; stageExpectedBytes = expected; outcome = "reading"; }
    }
    internal void Consumed(int count)
    {
        lock (sync) { stageBytes += count; wireBytes = Add(wireBytes, count); }
    }
    internal void MetadataReady() { lock (sync) { metadataComplete = true; outcome = "ready"; } }
    internal void End(string value) { lock (sync) outcome = value; }
    internal void PacketReady(ulong flags)
    {
        lock (sync)
        {
            if ((flags & (1UL << 63)) != 0) configPackets = Add(configPackets, 1);
            else
            {
                if ((flags & (1UL << 62)) != 0) keyPackets = Add(keyPackets, 1);
                else deltaPackets = Add(deltaPackets, 1);
                lastPts = flags & ((1UL << 62) - 1);
            }
            lastPacketUtc = DateTimeOffset.UtcNow; firstPacketUtc ??= lastPacketUtc; outcome = "ready";
        }
    }
    static long Add(long value, int count) => value > long.MaxValue - count ? long.MaxValue : value + count;
}
public sealed record ScrcpyReadSnapshot(string Stage, string Outcome, int StageBytes, int StageExpectedBytes,
    long WireBytes, bool MetadataComplete, long ConfigPackets, long KeyPackets, long DeltaPackets,
    DateTimeOffset? FirstPacketUtc, DateTimeOffset? LastPacketUtc, string? LastPts);
