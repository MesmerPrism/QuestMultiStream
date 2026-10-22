using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuestMultiStream.Browser;

// The MIT host consumes a framed stream from a separately selected Quest
// platform helper. No platform helper implementation is copied into this app.
public sealed record NativeCaptureSelection(string Schema, HostSelection Transport, OwnedPin Helper,
    string EntryClass, string DriverSha256, Dictionary<string,string>? DriverProfiles=null,
    bool GlobalNativeCaptureAuthorityAcknowledged=false)
{
    public const string Square = "square-left", Raw = "raw-two-lens";
    public static bool Mode(string? value) => value is Raw or Square;
    public void RequireDurableAdmission(bool hasOwnedSelection, bool hasCaptureOwner)
    {
        if (!hasOwnedSelection || !hasCaptureOwner)
            throw new InvalidDataException("Native capture requires the existing durable capture owner and owned selection");
    }
    public void RequireGlobalAuthority()
    {
        if (!GlobalNativeCaptureAuthorityAcknowledged)
            throw new InvalidDataException("Explicit sole global native capture authority acknowledgment required");
    }
    public void Validate(bool current = true)
    {
        RequireGlobalAuthority();
        if (Schema != "questms.native-square.selection.v1" || Transport is null || Helper is null ||
            EntryClass != "io.github.mesmerprism.rustyquest.nativecapture.NativeBrowserCapture" ||
            !Regex.IsMatch(DriverSha256 ?? "", "^[a-f0-9]{64}$"))
            throw new InvalidDataException("Explicit native square capture selection required");
        Transport.Validate(current);
        if(Transport.Sources.Select(s=>s.Canonical).Distinct().Count()!=Transport.Sources.Length)
            throw new InvalidDataException("Native sources must select distinct physical headsets");
        if(DriverProfiles is not null&&DriverProfiles.Any(p=>Source(p.Key) is null||!Regex.IsMatch(p.Value??"","^[a-f0-9]{64}$")))
            throw new InvalidDataException("Native Driver profiles must name exact selected sources and hashes");
        using var helper = OwnedDisplaySelection.Hold(Helper);
    }
    public SourceSelection? Source(string id) => Transport.Sources.SingleOrDefault(s => s.Id == id);
    public string DriverFor(string id)=>DriverProfiles?.GetValueOrDefault(id)??DriverSha256;
    public string[] Modes(string id) => Source(id) is null ? [Raw] : [Raw, Square];
    public void RequireSameTarget(HostSelection? raw, OwnedDisplaySelection? owned)
    {
        foreach (var source in Transport.Sources)
        {
            if (raw is not null && !raw.Sources.Any(s => s.Id == source.Id && s.Serial == source.Serial &&
                    s.Canonical == source.Canonical && s.Boot == source.Boot && s.LeaseId == source.LeaseId))
                throw new InvalidDataException("Native capture target differs from existing mirror selection");
            if (owned is not null)
            {
                var row=owned.Sources.SingleOrDefault(s=>s.SourceId==source.Id)
                    ??throw new InvalidDataException("Native capture source is outside the selected owner");
                using var held=OwnedDisplaySelection.Hold(row.OwnerBindingRef);
                if(held.Length>65536)throw new InvalidDataException("Native/raw target binding exceeds bound");
                var bytes=new byte[held.Length];held.ReadExactly(bytes);
                var binding=OwnerJson.Read<NativeRawOwnerBinding>(bytes);
                if(binding.Schema!="rusty.hostess.display_stream.owner_binding.v1"||
                    binding.Transport!=source.Serial||binding.Canonical!=source.Canonical||binding.BootId!=source.Boot||
                    !Path.GetFullPath(binding.Adb.Path).Equals(Path.GetFullPath(Transport.Adb),StringComparison.OrdinalIgnoreCase)||
                    binding.Adb.Sha256!=Transport.AdbSha256||binding.Adb.Bytes!=new FileInfo(Transport.Adb).Length)
                    throw new InvalidDataException("Native/raw owner binding selects a different physical target or ADB tool");
            }
        }
    }
}

internal sealed record NativeRawOwnerBinding(string Schema,OwnedPin Adb,OwnedPin ShellJar,string Transport,
    string Canonical,string BootId,int RemotePort,int DisplayId,int Width,int Height,int Fps,int BitrateBps);

internal sealed class NativeSquareCapture(NativeCaptureSelection selected, SourceSelection source) : ICaptureInvocation
{
    readonly object sync = new();
    public string InvocationId { get; } = Guid.NewGuid().ToString("N");
    public string Generation { get; } = Guid.NewGuid().ToString("N");
    readonly string nonce = Guid.NewGuid().ToString("N");
    public bool Started { get; private set; }
    public bool RetirementProven { get; private set; }
    bool stageAttempted;
    public bool RetirementPending => (Started || stageAttempted) && !RetirementProven;
    CaptureProducerFacts facts = new(false, false, false, 0, 0, null, null, null, null);
    public CaptureProducerFacts Facts() { lock (sync) return facts; }
    public CaptureTerminalFacts? Terminal { get; private set; }
    int stopSent;

    public async Task Run(CaptureFanout fanout, CancellationToken stop)
    {
        stop.ThrowIfCancellationRequested(); selected.Validate();
        if (!selected.Transport.Sources.Contains(source)) throw new InvalidDataException("Unselected native source");
        var config = selected.Transport;
        var driverSha=selected.DriverFor(source.Id);
        await config.Guard(source);
        using var helper = OwnedDisplaySelection.Hold(selected.Helper);
        using var adb = File.Open(config.Adb, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var board = File.Open(config.Board, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var powershell = File.Open(config.PowerShell, FileMode.Open, FileAccess.Read, FileShare.Read);
        var evidence = new CaptureEvidence(config.EvidenceDirectory, "native-square-" + InvocationId);
        string remote = "/data/local/tmp/questms-native-square-" + InvocationId + ".jar";
        evidence.Event("intent", new { source, captureMode = NativeCaptureSelection.Square,
            helper = selected.Helper, driverSha256=driverSha, selected.EntryClass, InvocationId, Generation,
            stopNonce = nonce, remote, nativeStopIsGlobalAndAsynchronous = true,
            soleCaptureOperatorRequired = true, noPrivacyOverride = true });
        Process? child = null; Task<string>? stderr = null; Task control = Task.CompletedTask;
        bool pushAttempted = false, stdoutEof = false; Exception? failure = null;
        using var deadline = MonitorLifetime.CaptureCancellation(config.DeadlineUtc, stop);
        using var monitorStop = new CancellationTokenSource();
        Task monitor = Task.CompletedTask;
        try
        {
            var path = (await HostSelection.Command(config.Adb, ["-s", source.Serial, "shell", "-T", "pm path com.oculus.systemdriver"])).Trim();
            if (!Regex.IsMatch(path, "^package:/[A-Za-z0-9_./@+=-]+\\.apk$")) throw new InvalidDataException("One installed Driver APK required");
            path = path[8..];
            var digest = await HostSelection.Command(config.Adb, ["-s", source.Serial, "shell", "-T", "sha256sum " + path]);
            if (digest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() != driverSha)
                throw new InvalidDataException("Installed Driver bytes differ from selected platform helper");
            deadline.Token.ThrowIfCancellationRequested();
            pushAttempted = true; stageAttempted = true; evidence.Event("stage-intent", new { remote });
            await HostSelection.Command(config.Adb, ["-s", source.Serial, "push", selected.Helper.Path, remote]);
            var staged = await HostSelection.Command(config.Adb, ["-s", source.Serial, "shell", "-T", "sha256sum " + remote]);
            if (staged.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() != selected.Helper.Sha256)
                throw new InvalidDataException("Staged native helper hash differs");
            await config.Guard(source); deadline.Token.ThrowIfCancellationRequested();
            var launch = "CLASSPATH='" + remote + "' exec /system/bin/app_process64 / " + selected.EntryClass +
                " --driver-apk " + path + " --driver-sha256 " + driverSha +
                " --invocation " + InvocationId + " --generation " + Generation + " --stop-nonce " + nonce + " --wire-encoding base64";
            var start = new ProcessStartInfo(config.Adb) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-s", source.Serial, "shell", "-T", launch }) start.ArgumentList.Add(arg);
            evidence.Event("launch-intent", new { InvocationId, Generation });
            child = Process.Start(start) ?? throw new IOException("Native square child not started"); Started = true;
            child.StandardInput.NewLine = "\n";
            stderr = DrainDiagnostics(child.StandardError);
            evidence.Event("started", new { child.Id, birth = child.StartTime.ToUniversalTime().Ticks });
            control = Control();
            monitor = Task.Run(async () => {
                try { while (true) { await Task.Delay(TimeSpan.FromSeconds(30), monitorStop.Token); await config.Guard(source); } }
                catch (OperationCanceledException) when (monitorStop.IsCancellationRequested) { }
                catch (Exception guardError) { failure ??= guardError; evidence.SafeEvent("guard-failed", guardError.Message); deadline.Cancel(); }
            });
            // adb shell on the tested Windows client expands LF in binary
            // stdout. ASCII base64 crosses that boundary without altering AVC;
            // decode before the unchanged framed video reader.
            using var media = DecodeMedia(child.StandardOutput.BaseStream);
            var reader = new OwnedVideoReader(media, InvocationId, Generation);
            var metadata = await reader.Header(CancellationToken.None);
            using (var json = JsonDocument.Parse(JsonSerializer.Serialize(metadata)))
                if (json.RootElement.GetProperty("Width").GetUInt32() != 1280 || json.RootElement.GetProperty("Height").GetUInt32() != 1280)
                    throw new InvalidDataException("Actual native square dimensions differ");
            fanout.SetMetadata(new { kind = "video", Width = 1280, Height = 1280, codec = "h264",
                captureMode = NativeCaptureSelection.Square, frameLayout = "mono" });
            lock (sync) facts = facts with { MetadataValidated = true };
            while (await reader.Packet(CancellationToken.None) is { } packet)
            {
                var stamp = BinaryPrimitives.ReadUInt64BigEndian(packet);
                lock (sync)
                {
                    var configPacket = (stamp & (1UL << 63)) != 0;
                    facts = configPacket ? facts with { Packets = facts.Packets + 1, ConfigObserved = true,
                        KeyObserved = false, VideoPackets = 0, PreviousPtsUs = null, LastPtsUs = null,
                        PreviousPacketUtc = null, LastPacketUtc = null } : facts with {
                        Packets = facts.Packets + 1, VideoPackets = facts.VideoPackets + 1,
                        KeyObserved = facts.KeyObserved || (stamp & (1UL << 62)) != 0,
                        PreviousPtsUs = facts.LastPtsUs, LastPtsUs = (stamp & ((1UL << 62) - 1)).ToString(),
                        PreviousPacketUtc = facts.LastPacketUtc, LastPacketUtc = DateTimeOffset.UtcNow };
                }
                fanout.Publish(packet);
            }
            stdoutEof = true;
        }
        catch (Exception error)
        {
            failure = error; evidence.SafeEvent("failure", new { error.Message });
            if (child is not null)
            {
                await RequestStop();
                try { await child.StandardOutput.BaseStream.CopyToAsync(Stream.Null); stdoutEof = true; }
                catch (Exception drain) { evidence.SafeEvent("stdout-unresolved", drain.Message); }
            }
        }
        finally
        {
            monitorStop.Cancel(); await monitor;
            if (child is not null)
            {
                await child.WaitForExitAsync(); await control;
                var diagnostics = await stderr!;
                evidence.SafeEvent("diagnostics", new { text = diagnostics });
                JsonElement? terminal = null; bool confirmed = false;
                try
                {
                    terminal = TerminalRecord(diagnostics, InvocationId, Generation, nonce);
                    confirmed = child.ExitCode == 0 && stdoutEof && terminal is { } value &&
                        value.GetProperty("state").GetString() == "retired" &&
                        value.GetProperty("stop_nonce_matched").GetBoolean() &&
                        value.GetProperty("own_resources_retired").GetBoolean() &&
                        value.GetProperty("native_stop_returned").GetBoolean() &&
                        !value.GetProperty("native_server_acknowledged").GetBoolean();
                }
                catch (Exception proofError) { failure ??= proofError; }
                Terminal = new(child.ExitCode, stdoutEof, true, child.ExitCode, stdoutEof, true,
                    confirmed, confirmed, config.EvidenceDirectory);
                evidence.SafeEvent("client-retired", new { child.ExitCode, stdoutEof, stderrEof = true,
                    clientStopNonceMatched = confirmed, nativeServerRetirementAcknowledged = false,
                    terminal, facts = Facts() });
                child.Dispose();
                if (!confirmed) failure ??= new IOException("Native capture client retirement not proven");
                RetirementProven = confirmed;
            }
            if (pushAttempted && (child is null || RetirementProven))
            {
                try
                {
                    config.Validate(false);
                    await HostSelection.Command(config.Adb, ["-s", source.Serial, "shell", "-T", "rm -f " + remote + " && test ! -e " + remote]);
                    evidence.SafeEvent("own-stage-removed", new { remote });
                    if (child is null) RetirementProven = true;
                }
                catch (Exception cleanup) { RetirementProven = false; failure ??= cleanup; }
            }
            (RetirementProven, failure) = RetainTerminalEvidence(evidence, RetirementProven, failure);
        }
        if (failure is not null) throw failure;

        async Task Control()
        {
            var signal = Task.Delay(Timeout.Infinite, deadline.Token);
            if (await Task.WhenAny(signal, child!.WaitForExitAsync()) == signal) await RequestStop();
        }
        async Task RequestStop()
        {
            if (Interlocked.Exchange(ref stopSent, 1) == 0 && child is { HasExited: false })
            {
                evidence.SafeEvent("stop-intent", new { InvocationId, Generation, stopNonce = nonce });
                try
                {
                    await child.StandardInput.WriteLineAsync("STOP " + nonce);
                    await child.StandardInput.FlushAsync();
                }
                catch (Exception controlError) { failure ??= controlError; evidence.SafeEvent("stop-write-failed", controlError.Message); }
            }
        }
    }

    internal static (bool Retired, Exception? Failure) RetainTerminalEvidence(
        CaptureEvidence evidence, bool retired, Exception? failure)
    {
        if (evidence.Errors.Count != 0) { retired = false; failure ??= new IOException("Native capture evidence retention failed"); }
        evidence.SafeEvent("terminal", new { retired, failure = failure?.Message,
            nativeServerRetirementAcknowledged = false });
        // SafeEvent records a create/flush failure without throwing. A failure
        // on this last append must hold the original just like an earlier one.
        if (evidence.Errors.Count != 0) { retired = false; failure ??= new IOException("Native capture evidence retention failed"); }
        return (retired, failure);
    }

    internal static Stream DecodeMedia(Stream stdout) => new CryptoStream(stdout,
        new FromBase64Transform(FromBase64TransformMode.IgnoreWhiteSpaces), CryptoStreamMode.Read, leaveOpen: true);

    internal static JsonElement? TerminalRecord(string text, string invocation, string generation, string nonce)
    {
        JsonElement? result = null;
        foreach (var line in text.Split('\n'))
        {
            if (!line.TrimStart().StartsWith('{')) continue;
            using var doc = JsonDocument.Parse(line);
            var row = doc.RootElement;
            if (!row.TryGetProperty("event", out var name) || name.GetString() != "TERMINAL") continue;
            if (result is not null || row.GetProperty("invocation").GetString() != invocation ||
                row.GetProperty("generation").GetString() != generation || row.GetProperty("stop_nonce").GetString() != nonce)
                throw new InvalidDataException("Native capture terminal identity differs");
            result = row.Clone();
        }
        return result;
    }
    static async Task<string> DrainDiagnostics(StreamReader reader)
    {
        var text = new System.Text.StringBuilder(); var chars = new char[4096]; int count; long total = 0;
        while ((count = await reader.ReadAsync(chars)) > 0)
        {
            total += count;
            if (total <= 256 * 1024) text.Append(chars, 0, count);
        }
        if (total > 256 * 1024) throw new InvalidDataException("Native diagnostics exceeded retained bound; EOF drained");
        return text.ToString();
    }
}
