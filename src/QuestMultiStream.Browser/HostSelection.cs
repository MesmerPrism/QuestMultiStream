using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using QuestMultiStream.Core.Models;
using QuestMultiStream.Core.Services;

namespace QuestMultiStream.Browser;

public sealed record SourceSelection(string Id, string Label, string Serial, string Canonical,
    string Boot, string LeaseId, int ForwardPort, int DisplayId = 0, string? Crop = null);
public sealed record HostSelection(bool Enabled, DateTimeOffset? DeadlineUtc, string Adb,
    string AdbSha256, string Server, string ServerSha256, string Board, string BoardSha256, string PowerShell, string PowerShellSha256,
    SourceSelection[] Sources, int DaemonPid, long DaemonStartUtcTicks, string EvidenceDirectory, string CaptureLifetime = MonitorLifetime.Bounded)
{
    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    public void Validate(bool requireCurrentDeadline = true)
    {
        if (!Enabled) throw new InvalidOperationException("Live capture was not explicitly enabled by the host operator");
        MonitorLifetime.ValidateCapture(CaptureLifetime, DeadlineUtc, DateTimeOffset.UtcNow, requireCurrentDeadline);
        if (!Path.IsPathFullyQualified(EvidenceDirectory) || !Directory.Exists(EvidenceDirectory)) throw new InvalidOperationException("Private evidence directory must already exist");
        using (var daemon = Process.GetProcessById(DaemonPid))
            if (daemon.HasExited || daemon.StartTime.ToUniversalTime().Ticks != DaemonStartUtcTicks || !daemon.ProcessName.Equals("adb", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Selected shared ADB daemon changed");
        foreach (var (path, hash) in new[] {(Adb, AdbSha256), (Server, ServerSha256), (Board, BoardSha256), (PowerShell, PowerShellSha256)})
            if (!Path.IsPathFullyQualified(path) || Hash(path) != hash) throw new InvalidOperationException("Pinned host tool changed");
        if (Sources.Length == 0 || Sources.Select(s => s.Id).Distinct().Count() != Sources.Length ||
            Sources.Select(s => s.ForwardPort).Distinct().Count() != Sources.Length) throw new InvalidOperationException("Source identity/ports are ambiguous");
        foreach (var s in Sources)
        {
            if (!Regex.IsMatch(s.Id, "^[a-zA-Z0-9_-]+$") || !Regex.IsMatch(s.Serial, "^[a-zA-Z0-9_.:-]+$") ||
                !Regex.IsMatch(s.Canonical, "^[a-zA-Z0-9]+$") || !Guid.TryParse(s.Boot, out _) || !Guid.TryParse(s.LeaseId, out _) ||
                s.ForwardPort is < 1024 or > 65535 || s.DisplayId < 0 ||
                (s.Crop is not null && !Regex.IsMatch(s.Crop, "^[1-9][0-9]*:[1-9][0-9]*:[0-9]+:[0-9]+$")))
                throw new InvalidOperationException("Invalid closed source configuration");
        }
    }
    public async Task Guard(SourceSelection s)
    {
        Validate();
        var leases = await Command(PowerShell, ["-NoProfile", "-NonInteractive", "-File", Board, "status", "--json"]);
        using var doc = JsonDocument.Parse(leases);
        if (!LeaseMatches(doc.RootElement, s, DateTimeOffset.UtcNow, DeadlineUtc))
            throw new InvalidOperationException("Exact Quest lease is not active");
        if ((await Command(Adb, ["-s", s.Serial, "shell", "getprop", "ro.serialno"])).Trim() != s.Canonical ||
            (await Command(Adb, ["-s", s.Serial, "shell", "cat", "/proc/sys/kernel/random/boot_id"])).Trim() != s.Boot)
            throw new InvalidOperationException("Canonical/boot identity changed");
        Validate(); // Guards themselves may cross expiry or daemon replacement.
    }
    public static bool LeaseMatches(JsonElement doc, SourceSelection s, DateTimeOffset now, DateTimeOffset? requiredUntil = null) =>
        doc.GetProperty("active_leases").EnumerateArray().Any(l =>
            l.GetProperty("id").GetString() == s.LeaseId && l.GetProperty("resource").GetString() == "quest:" + s.Canonical &&
            l.GetProperty("status").GetString() == "active" && DateTimeOffset.TryParse(l.GetProperty("lease_until").GetString(), out var until) && until > now && until >= (requiredUntil ?? now));
    public static async Task<string> Command(string executable, string[] arguments)
    {
        using var p = Start(executable, arguments);
        var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(); // No timeout may drop ownership of a started host child.
        var text = await output; var err = await error;
        if (p.ExitCode != 0) throw new InvalidOperationException($"Owned command failed {p.ExitCode}: {err}");
        return text;
    }
    public static Process Start(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Host child was not started");
    }
    public static string[] ServerArguments(SourceSelection s, string scid)
    {
        // Reuse the owner's closed display/crop launch profile; no browser shell text.
        var profile = new ScrcpyLaunchProfile { DisplayId = s.DisplayId, Crop = s.Crop,
            MaxSize = 1344, MaxFps = 30, VideoBitRateMbps = 12, EnableAudio = false,
            EnableControl = false, StayAwake = false };
        var device = new QuestDevice(s.Serial, s.Label, "", "", "", "device", QuestDeviceConnectionKind.Unknown, true, null);
        var ownerArguments = ScrcpyArgumentBuilder.Build(device, "browser", profile);
        var options = new List<string> {"3.3.4", "scid=" + scid, "log_level=info", "tunnel_forward=true",
            // The private second socket carries no events; EOF alone asks the
            // pinned server to stop even when no encoded frame has been produced.
            "audio=false", "control=true", "power_on=false", "clipboard_autosync=false", "cleanup=false", "max_size=1344", "max_fps=30", "video_bit_rate=12000000", "video_codec=h264"};
        foreach (var a in ownerArguments)
            if (a.StartsWith("--display-id=")) options.Add("display_id=" + a[13..]);
            else if (a.StartsWith("--crop=")) options.Add("crop=" + a[7..]);
        return options.ToArray();
    }
}
