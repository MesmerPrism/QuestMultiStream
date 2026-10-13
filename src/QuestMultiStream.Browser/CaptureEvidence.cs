using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
namespace QuestMultiStream.Browser;

public sealed class CaptureEvidence(string directory, string span)
{
    int sequence;
    public List<string> Errors { get; } = [];
    public void SafeEvent(string stage, object? detail = null)
    {
        try { Event(stage, detail); }
        catch (Exception e) { Errors.Add(stage + ": " + e.Message); try { Console.Error.WriteLine("Evidence failure: " + stage + ": " + e.Message); } catch {} }
    }
    public void Event(string stage, object? detail = null)
    {
        using var file = new FileStream(Path.Combine(directory, span + $".{++sequence:D3}.{stage}.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(file, new { utc = DateTimeOffset.UtcNow, stage, detail }); file.Flush(true);
    }
    public async Task<DrainResult> Drain(Stream stream, string name)
    {
        FileStream? file = null; string? captureError = null;
        try { file = new FileStream(Path.Combine(directory, span + "." + name + ".raw"), FileMode.CreateNew, FileAccess.Write, FileShare.Read); }
        catch (Exception e) { captureError = e.Message; }
        using var ownedFile = file;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[16384]; long bytes = 0, retained = 0;
        while (true)
        {
            var n = await stream.ReadAsync(buffer); if (n == 0) break;
            hash.AppendData(buffer, 0, n); bytes += n;
            var keep = (int)Math.Min(n, Math.Max(0, 1024 * 1024 - retained));
            if (keep > 0 && file is not null && captureError is null)
                try { await file.WriteAsync(buffer.AsMemory(0, keep)); retained += keep; } catch (Exception e) { captureError = e.Message; }
        }
        if (file is not null) try { file.Flush(true); } catch (Exception e) { captureError ??= e.Message; }
        return new DrainResult(bytes, retained, bytes != retained, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), true, captureError);
    }
}
public sealed record DrainResult(long Bytes, long Retained, bool Truncated, string FullStreamSha256, bool Eof, string? CaptureError);
