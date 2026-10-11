using System.Net;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuestMultiStream.Browser;

if (args.Contains("--self-test")) { await SelfTests.Run(); return; }
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:43118");
var app = builder.Build();
var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
if (args.Length % 2 != 0 || args.Where((_,i)=>i%2==0).Any(a=>a is not "--selection" and not "--fixture" and not "--host-end") || args.Where((_,i)=>i%2==0).Distinct().Count()!=args.Length/2)
    throw new InvalidOperationException("Only explicit --selection, --fixture and --host-end inputs are accepted");
string? Option(string key) { var i = Array.IndexOf(args,key); return i < 0 ? null : args[i+1]; }
var selectionPath = Option("--selection");
using var selectionLock = selectionPath is null ? null : File.Open(selectionPath, FileMode.Open, FileAccess.Read, FileShare.Read);
HostSelection? selection = selectionPath is null ? null : JsonSerializer.Deserialize<HostSelection>(File.ReadAllText(selectionPath));
selection?.Validate();
var hostEnd = Option("--host-end") is string end ? DateTimeOffset.Parse(end) : (DateTimeOffset?)null;
if(hostEnd is not null && (hostEnd <= DateTimeOffset.UtcNow || selection is not null && hostEnd < selection.DeadlineUtc)) throw new InvalidOperationException("Host end must be future and at or after selected capture deadline");
var fixture = Option("--fixture");
if (fixture is not null && (selection is not null || !Path.IsPathFullyQualified(fixture) || !File.Exists(fixture))) throw new InvalidOperationException("Fixture requires offline mode and an existing selected absolute file");
var busy = new HashSet<string>(); var sync = new object();
app.UseWebSockets(); app.UseDefaultFiles(); app.UseStaticFiles();
bool Authorized(HttpContext c) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(c.Request.Query["token"].ToString()), Encoding.ASCII.GetBytes(token));
app.MapGet("/api/sources", (HttpContext c) => Authorized(c) ? Results.Json(selection?.Sources.Select(s => new {s.Id, s.Label, s.DisplayId, s.Crop}) ?? []) : Results.Unauthorized());
app.MapGet("/api/fixture", (HttpContext c) => Authorized(c) ? Results.Json(new { available = fixture is not null }) : Results.Unauthorized());
app.MapGet("/fixture", (HttpContext c) => Authorized(c) && fixture is not null ? Results.File(fixture, "video/mp4", enableRangeProcessing: true) : Results.NotFound());
app.MapGet("/stream/{id}", async (HttpContext context, string id) =>
{
    if (!Authorized(context) || context.Request.Headers.Origin != "http://127.0.0.1:43118" || !context.WebSockets.IsWebSocketRequest)
    { context.Response.StatusCode = 403; return; }
    var source = selection?.Sources.SingleOrDefault(s => s.Id == id);
    if(hostEnd is not null && DateTimeOffset.UtcNow >= hostEnd){context.Response.StatusCode=410;return;}
    if (source is null) { context.Response.StatusCode = 404; return; }
    lock(sync) { if (!busy.Add(id)) { context.Response.StatusCode = 409; return; } }
    try
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await Capture(socket, selection!, source);
    }
    catch(Exception error) { app.Logger.LogError(error, "Selected capture failed; no replay"); }
    finally { lock(sync) busy.Remove(id); }
});
Console.WriteLine($"Browser viewer: http://127.0.0.1:43118/#token={token}");
Console.WriteLine(selection is null ? "OFFLINE: file/window fallback only; no ADB/device operations" : "Explicit selected device capture; no controls, audio, wake or retries");
if(hostEnd is not null) _ = Task.Run(async()=>{
    await Task.Delay(hostEnd.Value-DateTimeOffset.UtcNow);
    while(true){lock(sync){if(busy.Count==0){app.Lifetime.StopApplication();return;}}
        Console.Error.WriteLine("HELD: host end reached but actual capture ownership remains; do not release port lease");await Task.Delay(TimeSpan.FromSeconds(30));}
});
await app.RunAsync();

static async Task Capture(WebSocket socket, HostSelection config, SourceSelection source)
{
    await config.Guard(source);
    using var adbLock = File.Open(config.Adb, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var serverLock = File.Open(config.Server, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var pwshLock = File.Open(config.PowerShell, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var boardLock = File.Open(config.Board, FileMode.Open, FileAccess.Read, FileShare.Read);
    config.Validate();
    var scid = RandomNumberGenerator.GetInt32(1, int.MaxValue).ToString("x8");
    var remote = "/data/local/tmp/questms-browser-" + scid + ".jar";
    var evidence = new CaptureEvidence(config.EvidenceDirectory, "capture-" + scid);
    evidence.Event("intent", new { source, remote, config.DeadlineUtc, config.DaemonPid, config.DaemonStartUtcTicks, config.AdbSha256, config.ServerSha256 });
    Process? child = null; Task<DrainResult>? output = null, error = null;
    TcpClient? tcp = null; var forwarded = false; var pushed = false;
    Exception? failure = null;
    using var deadline = new CancellationTokenSource(config.DeadlineUtc - DateTimeOffset.UtcNow);
    Task? monitor = null;
    using var receiveStop = new CancellationTokenSource();
    Task? receive = null; var browserClosed = false;
    try
    {
        evidence.Event("push-intent");
        await HostSelection.Command(config.Adb, ["-s", source.Serial, "push", config.Server, remote]); pushed = true;
        evidence.Event("forward-intent");
        await HostSelection.Command(config.Adb, ["-s", source.Serial, "forward", "--no-rebind", "tcp:" + source.ForwardPort, "localabstract:scrcpy_" + scid]); forwarded = true;
        await config.Guard(source);
        evidence.Event("server-start-intent");
        child = HostSelection.Start(config.Adb, ["-s", source.Serial, "shell", "CLASSPATH=" + remote, "app_process", "/", "com.genymobile.scrcpy.Server", .. HostSelection.ServerArguments(source, scid)]);
        output = evidence.Drain(child.StandardOutput.BaseStream, "stdout"); error = evidence.Drain(child.StandardError.BaseStream, "stderr");
        evidence.SafeEvent("server-started", new { pid = child.Id, birth = child.StartTime.ToUniversalTime().Ticks });
        monitor = Task.Run(async () => {
            try { while (true) { await Task.Delay(TimeSpan.FromSeconds(30), deadline.Token); await config.Guard(source); } }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) {}
            catch (Exception e) { failure = e; deadline.Cancel(); }
        });
        receive = Task.Run(async () => {
            try { await BrowserConnection.ObserveClose(socket, receiveStop.Token, () => { browserClosed = true; deadline.Cancel(); }); }
            catch (OperationCanceledException) when (receiveStop.IsCancellationRequested) {}
            catch (Exception e) { failure = e; deadline.Cancel(); }
        });
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token); startup.CancelAfter(TimeSpan.FromSeconds(10));
        tcp = await ScrcpyStartup.Connect(source.ForwardPort, () => child.HasExited, startup.Token, s => evidence.SafeEvent("startup", s));
        var reader = new ScrcpyVideoReader(tcp.GetStream()); var size = await reader.Header(deadline.Token, dummyAlreadyRead: true);
        evidence.SafeEvent("video-ready", new { size.Width, size.Height });
        await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {kind="video", size.Width, size.Height, codec="h264"})), WebSocketMessageType.Text, true, deadline.Token);
        while (socket.State == WebSocketState.Open)
        {
            var packet = await reader.Packet(deadline.Token); if (packet is null) break;
            await socket.SendAsync(packet, WebSocketMessageType.Binary, true, deadline.Token);
        }
    }
    catch (OperationCanceledException) when (browserClosed && failure is null) { evidence.SafeEvent("browser-close-observed"); }
    catch (Exception e) { failure ??= e; evidence.SafeEvent("capture-failed", new { type = e.GetType().Name, e.Message }); }
    finally
    {
        tcp?.Dispose();
        deadline.Cancel();
        receiveStop.Cancel();
        if (receive is not null) await receive;
        if (monitor is not null) await monitor;
        if (child is not null)
        {
            if (!child.HasExited) evidence.SafeEvent("HELD", new { pid = child.Id, reason = "Awaiting actual server child exit; source locks retained; no kill/replay" });
            await child.WaitForExitAsync(); // Uncertain original child keeps SAME invocation and source ownership.
            DrainResult stdout, stderr;
            try { stdout = await output!; stderr = await error!; }
            catch (Exception e) { evidence.SafeEvent("HELD", new { reason = "Actual pipe EOF unproven; source handles remain held", error = e.Message }); await Task.Delay(Timeout.Infinite); throw; }
            if (stdout.CaptureError is not null || stderr.CaptureError is not null) failure ??= new IOException("Raw evidence retention failed; actual pipes drained through EOF");
            evidence.SafeEvent("server-retired", new { exit = child.ExitCode, stdout, stderr, bothEOF = true, reaped = true });
            if (child.ExitCode != 0) failure ??= new IOException("Selected server exited " + child.ExitCode);
            child.Dispose();
        }
        // Each cleanup attempt is independent; failure must not skip subsequent owned cleanup.
        if (forwarded) try { config.Validate(requireCurrentDeadline: false); await HostSelection.Command(config.Adb, ["-s",source.Serial,"forward","--remove","tcp:"+source.ForwardPort]); evidence.SafeEvent("forward-removed"); }
            catch (Exception e) { failure ??= e; evidence.SafeEvent("forward-cleanup-failed", e.Message); }
        if (pushed) try { config.Validate(requireCurrentDeadline: false); await HostSelection.Command(config.Adb, ["-s",source.Serial,"shell","rm",remote]); evidence.SafeEvent("jar-removed"); }
            catch (Exception e) { failure ??= e; evidence.SafeEvent("jar-cleanup-failed", e.Message); }
        if (evidence.Errors.Count > 0) failure ??= new IOException("Stage evidence retention failed");
        evidence.SafeEvent("terminal", new { success = failure is null, failure = failure?.Message, evidence_errors = evidence.Errors });
        if (evidence.Errors.Count > 0) failure ??= new IOException("Stage evidence retention failed");
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived) await socket.CloseOutputAsync(failure is null ? WebSocketCloseStatus.NormalClosure : WebSocketCloseStatus.InternalServerError, failure is null ? "Selected capture ended" : "Capture failed; inspect retained host evidence", CancellationToken.None);
    }
    if (failure is not null) throw failure;
}
