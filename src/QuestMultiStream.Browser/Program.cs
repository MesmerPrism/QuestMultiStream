using System.Net;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuestMultiStream.Browser;

if (args.Contains("--self-test")) { await SelfTests.Run(); return; }
if(args.Contains("--hub-action")) {
    if(args.Length%2!=0 || args.Where((_,i)=>i%2==0).Any(a=>a is not "--hub-action" and not "--hub-evidence" and not "--hub-owner" and not "--hub-bridge") || args.Where((_,i)=>i%2==0).Distinct().Count()!=args.Length/2)
        throw new InvalidDataException("Typed action CLI accepts only explicit action and owner selections");
    string? CliOption(string key) {var i=Array.IndexOf(args,key);return i<0?null:args[i+1];}
    if(CliOption("--hub-evidence") is not null && CliOption("--hub-owner") is not null)throw new InvalidDataException("Choose recorded evidence or selected owner projection");
    var path=CliOption("--hub-action")!;
    if(!Path.IsPathFullyQualified(path))throw new InvalidDataException("Selected absolute action file required");
    using var input=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read);
    if(input.Length>4096)throw new InvalidDataException("Bounded action file required");
    var request=JsonSerializer.Deserialize<HubAction>(input,HubControl.Json)??throw new InvalidDataException("Missing action");
    using var cliProjection=CliOption("--hub-owner") is {} cp?new HubProjectionSource(cp):null;
    using var cliBridge=CliOption("--hub-bridge") is {} cb?new HubOwnerBridge(cb):null;
    var handler=new HubActionHandler(new HubControl(CliOption("--hub-evidence"),cliProjection),cliBridge);
    Console.WriteLine(JsonSerializer.Serialize(await handler.Handle(request),HubControl.Json));
    return;
}
using var operatorStop = new CancellationTokenSource();
var builder = WebApplication.CreateBuilder(args);
// Routine request/static-file information includes query authentication tokens
// and overwhelms a standing observer. Preserve warnings/errors and the separate
// owned capture/control/native lifecycle journals and genuine EOF accounting.
builder.Logging.AddFilter("Microsoft.AspNetCore",LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime",LogLevel.Warning);
builder.Services.AddSingleton<IHostLifetime>(new OwnedConsoleLifetime(operatorStop.Cancel));
var portInput = Array.IndexOf(args, "--port");
var port = portInput < 0 ? 43118 : int.Parse(args[portInput + 1]);
if (port is < 1024 or > 65535) throw new InvalidOperationException("Port outside operator range");
var origin = $"http://127.0.0.1:{port}";
builder.WebHost.UseUrls(origin);
var app = builder.Build();
var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
if (args.Length % 2 != 0 || args.Where((_,i)=>i%2==0).Any(a=>a is not "--selection" and not "--fixture" and not "--host-end" and not "--port" and not "--hub-evidence" and not "--hub-bridge" and not "--hub-owner") || args.Where((_,i)=>i%2==0).Distinct().Count()!=args.Length/2)
    throw new InvalidOperationException("Only explicit --selection, --fixture and --host-end inputs are accepted");
string? Option(string key) { var i = Array.IndexOf(args,key); return i < 0 ? null : args[i+1]; }
var selectionPath = Option("--selection");
using var selectionLock = selectionPath is null ? null : File.Open(selectionPath, FileMode.Open, FileAccess.Read, FileShare.Read);
HostSelection? selection = selectionPath is null ? null : JsonSerializer.Deserialize<HostSelection>(File.ReadAllText(selectionPath), new JsonSerializerOptions { Converters = { new StrictUtcDateConverter() } });
selection?.Validate();
var hostEndInput = Option("--host-end");
if (selection is not null && hostEndInput is null) throw new InvalidOperationException("Live host requires an explicit lifetime");
if (selection?.CaptureLifetime == MonitorLifetime.UntilStop && hostEndInput != MonitorLifetime.UntilStop) throw new InvalidOperationException("Until-stop capture requires until-stop host");
var hostEnd = hostEndInput is null or MonitorLifetime.UntilStop ? (DateTimeOffset?)null : MonitorLifetime.ParseUtc(hostEndInput);
if(hostEnd is not null && (hostEnd <= DateTimeOffset.UtcNow || selection is not null && hostEnd < selection.DeadlineUtc)) throw new InvalidOperationException("Host end must be future and at or after selected capture deadline");
var fixture = Option("--fixture");
if(Option("--hub-evidence") is not null && Option("--hub-owner") is not null)throw new InvalidDataException("Choose recorded evidence or selected owner projection");
using var projection = Option("--hub-owner") is {} projectionPath?new HubProjectionSource(projectionPath):null;
var hub = new HubControl(Option("--hub-evidence"),projection);
using var hubBridge = Option("--hub-bridge") is { } bridgePath ? new HubOwnerBridge(bridgePath) : null;
var hubActions = new HubActionHandler(hub,hubBridge);
if (fixture is not null && (selection is not null || !Path.IsPathFullyQualified(fixture) || !File.Exists(fixture))) throw new InvalidOperationException("Fixture requires offline mode and an existing selected absolute file");
var busy = new HashSet<string>(); var sync = new object();
var fanouts = new Dictionary<string,CaptureFanout>();
var captureStops = new Dictionary<string,CancellationTokenSource>();
var captureStates = new Dictionary<string,string>();
var captureRequests = new Dictionary<string,(string Action,string Source,object Response)>();

_ = Task.Run(async () => { try { await Task.Delay(Timeout.Infinite, operatorStop.Token); } catch (OperationCanceledException) {} await MonitorLifetime.StopAfterRetirement(() => { lock(sync) return busy.Count == 0; }, app.Lifetime.StopApplication); });
app.UseWebSockets(); app.UseDefaultFiles(); app.UseStaticFiles();
bool Authorized(HttpContext c) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(c.Request.Query["token"].ToString()), Encoding.ASCII.GetBytes(token));
app.MapGet("/api/sources", (HttpContext c) => Authorized(c) ? Results.Json(selection?.Sources.Select(s => new {s.Id, s.Label, s.DisplayId, s.Crop}) ?? []) : Results.Unauthorized());
app.MapGet("/api/fixture", (HttpContext c) => Authorized(c) ? Results.Json(new { available = fixture is not null }) : Results.Unauthorized());
app.MapGet("/api/captures", (HttpContext c) => {
    if(!Authorized(c))return Results.Unauthorized();
    lock(sync)return Results.Json(captureStates.Select(pair=>new {id=pair.Key,state=pair.Value,retirementPending=busy.Contains(pair.Key)}).ToArray());
});
app.MapPost("/api/captures/{id}/{action}", async (HttpContext c,string id,string action) => {
    if(!Authorized(c)||c.Request.Headers.Origin!=origin)return Results.Unauthorized();
    if(c.Request.ContentLength is null or >4096 || action is not "start" and not "stop")return Results.BadRequest();
    CaptureOperatorRequest? request;
    try {request=await JsonSerializer.DeserializeAsync<CaptureOperatorRequest>(c.Request.Body,HubControl.Json);}
    catch(JsonException){return Results.BadRequest();}
    if(request is null || !Guid.TryParseExact(request.RequestId,"D",out _))return Results.BadRequest();
    var source=selection?.Sources.SingleOrDefault(s=>s.Id==id);
    if(source is null)return Results.NotFound();
    lock(sync) {
        if(captureRequests.TryGetValue(request.RequestId,out var prior))return prior.Action==action&&prior.Source==id?Results.Json(prior.Response):Results.Conflict();
        if(captureRequests.Count>=256)return Results.Conflict(new {state="request-capacity"});
        if(operatorStop.IsCancellationRequested||hostEnd is not null&&DateTimeOffset.UtcNow>=hostEnd)return Results.StatusCode(410);
        object response;
        if(action=="stop") {
            if(captureStops.TryGetValue(id,out var stop)){stop.Cancel();captureStates[id]="stopping";}
            response=new {state=busy.Contains(id)?"stopping":"retired",source=id,retirementPending=busy.Contains(id)};
        } else if(busy.Contains(id))response=new {state=captureStates.GetValueOrDefault(id,"running"),source=id,newCapture=false};
        else {
            busy.Add(id);var fanout=new CaptureFanout();fanouts[id]=fanout;
            _=fanout.Metadata.Task.ContinueWith(task=>{if(task.Status==TaskStatus.RanToCompletion)lock(sync){if(captureStates.GetValueOrDefault(id)=="requested")captureStates[id]="connected";}},TaskScheduler.Default);
            var stop=CancellationTokenSource.CreateLinkedTokenSource(operatorStop.Token);captureStops[id]=stop;captureStates[id]="requested";
            _=Task.Run(async()=>{
                Exception? failure=null;
                try {await Capture(null,selection!,source,stop.Token,fanout);}
                catch(Exception e){failure=e;app.Logger.LogError(e,"Owned capture ended with failure; no replay");}
                finally {fanout.Complete(failure);lock(sync){busy.Remove(id);captureStates[id]=failure is null?"retired":"failed";captureStops.Remove(id);}stop.Dispose();}
            });
            response=new {state="requested",source=id,newCapture=true};
        }
        captureRequests.Add(request.RequestId,(action,id,response));return Results.Json(response);
    }
});
app.MapGet("/observe/{id}",async (HttpContext c,string id)=>{
    if(!Authorized(c)||c.Request.Headers.Origin!=origin||!c.WebSockets.IsWebSocketRequest){c.Response.StatusCode=403;return;}
    CaptureFanout? fanout;lock(sync)fanouts.TryGetValue(id,out fanout);
    if(fanout is null){c.Response.StatusCode=404;return;}
    using var socket=await c.WebSockets.AcceptWebSocketAsync();using var stop=new CancellationTokenSource();
    Task? receive=null;
    try {
        using var observer=fanout.Subscribe();
        using var delivery=CancellationTokenSource.CreateLinkedTokenSource(stop.Token,observer.Closed);
        receive=Task.Run(async()=>{try{await BrowserConnection.ObserveClose(socket,stop.Token,stop.Cancel);}catch{stop.Cancel();}});
        var metadata=await fanout.Metadata.Task.WaitAsync(delivery.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata)),WebSocketMessageType.Text,true,delivery.Token);
        await foreach(var packet in observer.Read(delivery.Token))await socket.SendAsync(packet,WebSocketMessageType.Binary,true,delivery.Token);
        if(socket.State==WebSocketState.Open)await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"Owned capture ended",CancellationToken.None);
    } catch(Exception e) when(e is OperationCanceledException or WebSocketException or IOException or InvalidOperationException) {
        socket.Abort(); // Only this observer delivery. Never cancels the producer.
    } finally {stop.Cancel();if(receive is not null)await receive;}
});
app.MapGet("/api/hub", (HttpContext c) => Authorized(c) ? Results.Json(hub.Status(DateTimeOffset.UtcNow,hubBridge is not null,hubBridge?.Actions)) : Results.Unauthorized());
app.MapGet("/api/hub/capabilities", (HttpContext c) => Authorized(c) ? Results.Json(new { bridgeSelected = hubBridge is not null, direction = "both", actions=hubBridge?.Actions??["establish","disconnect"],execution = "Owner must authenticate live authority; selection alone proves no readiness" }) : Results.Unauthorized());
app.MapGet("/api/hub/actions/{requestId}", async (HttpContext c,string requestId) => {
    if (!Authorized(c)) return Results.Unauthorized();
    if (hubBridge is null) return Results.Json(new { error = "App-owner executor unavailable" },statusCode:503);
    try { return Results.Json(await hubBridge.Status(requestId)); }
    catch (InvalidDataException e) { return Results.BadRequest(new { error=e.Message }); }
    catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException) { return Results.Json(new { error="Owner readback unavailable; original action remains owned" },statusCode:502); }
});
app.MapPost("/api/hub/actions", async (HttpContext c) => {
    if (!Authorized(c) || c.Request.Headers.Origin != origin) return Results.Unauthorized();
    if (c.Request.ContentLength is null or > 4096) return Results.BadRequest(new { error = "Bounded JSON request required" });
    try {
        var request = await JsonSerializer.DeserializeAsync<HubAction>(c.Request.Body, HubControl.Json);
        if (request?.Execute == true) {
            if (hubBridge is null) return Results.Json(new { error = "App-owner executor unavailable; device effects denied" }, statusCode:503);
            return Results.Json(await hubActions.Handle(request));
        }
        var result = await hubActions.Handle(request ?? throw new InvalidDataException("Missing action"));
        return Results.Json(result);
    } catch (Exception e) when (e is JsonException or InvalidDataException) { return Results.BadRequest(new { error = e.Message }); }
      catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException) { return Results.Json(new { error = "Owner outcome unresolved; do not retry with a new request ID", detail = e.GetType().Name }, statusCode:502); }
});
app.MapGet("/fixture", (HttpContext c) => Authorized(c) && fixture is not null ? Results.File(fixture, "video/mp4", enableRangeProcessing: true) : Results.NotFound());
app.MapPost("/operator-stop", (HttpContext c) => { if (!Authorized(c) || c.Request.Headers.Origin != origin) return Results.Unauthorized(); operatorStop.Cancel(); return Results.Accepted(value: new { state = "requested", actual_retirement_pending = true }); });
app.MapGet("/stream/{id}", async (HttpContext context, string id) =>
{
    if (!Authorized(context) || context.Request.Headers.Origin != origin || !context.WebSockets.IsWebSocketRequest)
    { context.Response.StatusCode = 403; return; }
    var source = selection?.Sources.SingleOrDefault(s => s.Id == id);
    if(hostEnd is not null && DateTimeOffset.UtcNow >= hostEnd){context.Response.StatusCode=410;return;}
    if (source is null) { context.Response.StatusCode = 404; return; }
    lock(sync) { if (operatorStop.IsCancellationRequested) { context.Response.StatusCode = 410; return; } if (!busy.Add(id)) { context.Response.StatusCode = 409; return; } }
    using var legacyStop=CancellationTokenSource.CreateLinkedTokenSource(operatorStop.Token);
    lock(sync)captureStops[id]=legacyStop;
    try
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var fanout=new CaptureFanout();lock(sync){fanouts[id]=fanout;captureStates[id]="running";}
        try{await Capture(socket, selection!, source, legacyStop.Token,fanout);}finally{fanout.Complete();}
    }
    catch(Exception error) { app.Logger.LogError(error, "Selected capture failed; no replay"); }
    finally { lock(sync){busy.Remove(id);captureStops.Remove(id);captureStates[id]="retired";} }
});
Console.WriteLine($"Browser viewer: {origin}/#token={token}");
Console.WriteLine(selection is null ? "OFFLINE: file/window fallback only; no ADB/device operations" : "Explicit selected device capture; no controls, audio, wake or retries");
if(hostEnd is not null) _ = Task.Run(async()=>{
    await Task.Delay(hostEnd.Value-DateTimeOffset.UtcNow);
    while(true){lock(sync){if(busy.Count==0){app.Lifetime.StopApplication();return;}}
        Console.Error.WriteLine("HELD: host end reached but actual capture ownership remains; do not release port lease");await Task.Delay(TimeSpan.FromSeconds(30));}
});
await app.RunAsync();

static async Task Capture(WebSocket? socket, HostSelection config, SourceSelection source, CancellationToken operatorStop,CaptureFanout fanout)
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
    evidence.Event("intent", new { source, remote, config.DeadlineUtc, config.DaemonPid, config.DaemonStartUtcTicks, config.AdbSha256, config.ServerSha256, config.CaptureLifetime });
    Process? child = null; Task<DrainResult>? output = null, error = null;
    TcpClient? tcp = null; var forwarded = false; var pushed = false;
    Exception? failure = null;
    using var deadline = MonitorLifetime.CaptureCancellation(config.DeadlineUtc, operatorStop);
    Task? monitor = null;
    using var receiveStop = new CancellationTokenSource();
    Task? receive = null; var browserClosed = false;
    try
    {
        deadline.Token.ThrowIfCancellationRequested(); evidence.Event("push-intent");
        await HostSelection.Command(config.Adb, ["-s", source.Serial, "push", config.Server, remote]); pushed = true;
        deadline.Token.ThrowIfCancellationRequested(); evidence.Event("forward-intent");
        await HostSelection.Command(config.Adb, ["-s", source.Serial, "forward", "--no-rebind", "tcp:" + source.ForwardPort, "localabstract:scrcpy_" + scid]); forwarded = true;
        await config.Guard(source);
        deadline.Token.ThrowIfCancellationRequested(); evidence.Event("server-start-intent");
        child = HostSelection.Start(config.Adb, ["-s", source.Serial, "shell", "CLASSPATH=" + remote, "app_process", "/", "com.genymobile.scrcpy.Server", .. HostSelection.ServerArguments(source, scid)]);
        output = evidence.Drain(child.StandardOutput.BaseStream, "stdout"); error = evidence.Drain(child.StandardError.BaseStream, "stderr");
        evidence.SafeEvent("server-started", new { pid = child.Id, birth = child.StartTime.ToUniversalTime().Ticks });
        monitor = Task.Run(async () => {
            try { while (true) { await Task.Delay(TimeSpan.FromSeconds(30), deadline.Token); await config.Guard(source); } }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) {}
            catch (Exception e) { failure = e; deadline.Cancel(); }
        });
        if(socket is not null) receive = Task.Run(async () => {
            try { await BrowserConnection.ObserveClose(socket, receiveStop.Token, () => { browserClosed = true; deadline.Cancel(); }); }
            catch (OperationCanceledException) when (receiveStop.IsCancellationRequested) {}
            catch (Exception e) { failure = e; deadline.Cancel(); }
        });
        tcp = await ScrcpyStartup.Connect(source.ForwardPort, () => child.HasExited, deadline.Token, s => evidence.SafeEvent("startup", s));
        var reader = new ScrcpyVideoReader(tcp.GetStream()); var size = await reader.Header(deadline.Token, dummyAlreadyRead: true);
        evidence.SafeEvent("video-ready", new { size.Width, size.Height });
        var metadata=new {kind="video",size.Width,size.Height,codec="h264"};fanout.SetMetadata(metadata);
        if(socket is not null)await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata)), WebSocketMessageType.Text, true, deadline.Token);
        while (socket is null || socket.State == WebSocketState.Open)
        {
            var packet = await reader.Packet(deadline.Token); if (packet is null) break;
            fanout.Publish(packet);
            if(socket is not null)await socket.SendAsync(packet, WebSocketMessageType.Binary, true, deadline.Token);
        }
    }
    catch (OperationCanceledException) when (MonitorLifetime.OperatorStopWithinScope(operatorStop.IsCancellationRequested, config.DeadlineUtc, DateTimeOffset.UtcNow) && failure is null) { evidence.SafeEvent("operator-stop-observed"); }
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
        var cleanupUnknown=false;
        if (forwarded) try { config.Validate(requireCurrentDeadline: false); await HostSelection.Command(config.Adb, ["-s",source.Serial,"forward","--remove","tcp:"+source.ForwardPort]); evidence.SafeEvent("forward-removed"); }
            catch (Exception e) { cleanupUnknown=true;failure ??= e; evidence.SafeEvent("forward-cleanup-failed", e.Message); }
        if (pushed) try { config.Validate(requireCurrentDeadline: false); await HostSelection.Command(config.Adb, ["-s",source.Serial,"shell","rm",remote]); evidence.SafeEvent("jar-removed"); }
            catch (Exception e) {cleanupUnknown=true; failure ??= e; evidence.SafeEvent("jar-cleanup-failed", e.Message); }
        if (evidence.Errors.Count > 0) failure ??= new IOException("Stage evidence retention failed");
        if(cleanupUnknown||evidence.Errors.Count>0){evidence.SafeEvent("HELD",new {reason="Owned cleanup/retained evidence unproven; same invocation/tool locks retained; no kill/replay"});await Task.Delay(Timeout.Infinite);}
        evidence.SafeEvent("terminal", new { success = failure is null, failure = failure?.Message, evidence_errors = evidence.Errors });
        if (evidence.Errors.Count > 0) failure ??= new IOException("Stage evidence retention failed");
        if (socket?.State is WebSocketState.Open or WebSocketState.CloseReceived) await socket.CloseOutputAsync(failure is null ? WebSocketCloseStatus.NormalClosure : WebSocketCloseStatus.InternalServerError, failure is null ? "Selected capture ended" : "Capture failed; inspect retained host evidence", CancellationToken.None);
    }
    if (failure is not null) throw failure;
}

public sealed record CaptureOperatorRequest(string RequestId);
