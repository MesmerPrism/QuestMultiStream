using System.Net;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuestMultiStream.Browser;

if (args.Contains("--self-test")) { await SelfTests.Run(); return; }
if (args.Contains("--owner-v2-self-test")) { await OwnerV2Tests.Run(); return; }
if (args.Contains("--owner-v2-fixture")) { await OwnerV2Tests.ServeFixture(); return; }
if (args.Length is 2 or 3&&args[0]=="--owner-v2-read-test") { await OwnerV2Tests.ReadWire(args[1],args.Length==3?args[2]:null); return; }
if(args.Contains("--native-square-self-test")){await NativeSquareCaptureTests.Run(args.Length>=2?args[1]:null,args.Length==3?args[2]:null);return;}
if(args.Contains("--owner-auth-self-test")){await OwnedCandidateTests.AuthTest();return;}
if(args.Contains("--capture-owner-self-test")){await CaptureOwnerTests.Run();return;}
if(args.Length==2&&args[0]=="--capture-owner-fixtures"){await CaptureOwnerTests.Export(args[1]);return;}
if(args.Contains("--candidate-self-test")){await OwnedCandidateTests.Run(args.Length==3&&args[1]=="--fixture-python"?args[2]:null);return;}
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
if (args.Length % 2 != 0 || args.Where((_,i)=>i%2==0).Any(a=>a is not "--selection" and not "--owned-selection" and not "--native-capture" and not "--capture-owner" and not "--fixture" and not "--host-end" and not "--port" and not "--hub-evidence" and not "--hub-bridge" and not "--hub-owner" and not "--hub-session-owner") || args.Where((_,i)=>i%2==0).Distinct().Count()!=args.Length/2)
    throw new InvalidOperationException("Only explicit --selection, --fixture and --host-end inputs are accepted");
string? Option(string key) { var i = Array.IndexOf(args,key); return i < 0 ? null : args[i+1]; }
var selectionPath = Option("--selection");
using var selectionLock = selectionPath is null ? null : File.Open(selectionPath, FileMode.Open, FileAccess.Read, FileShare.Read);
HostSelection? selection = selectionPath is null ? null : JsonSerializer.Deserialize<HostSelection>(File.ReadAllText(selectionPath), new JsonSerializerOptions { Converters = { new StrictUtcDateConverter() } });
selection?.Validate();
var ownedPath=Option("--owned-selection");
using var ownedLock=ownedPath is null?null:File.Open(ownedPath,FileMode.Open,FileAccess.Read,FileShare.Read);
if(ownedLock?.Length>65536)throw new InvalidDataException("Owned selection exceeds bound");
OwnedDisplaySelection? owned=ownedPath is null?null:OwnerJson.Read<OwnedDisplaySelection>(File.ReadAllBytes(ownedPath));
if(owned is not null&&selection is not null)throw new InvalidDataException("Choose one producer implementation");owned?.Validate();
var nativePath=Option("--native-capture");
using var nativeLock=nativePath is null?null:File.Open(nativePath,FileMode.Open,FileAccess.Read,FileShare.Read);
if(nativeLock?.Length>65536)throw new InvalidDataException("Native capture selection exceeds bound");
NativeCaptureSelection? native=nativePath is null?null:OwnerJson.Read<NativeCaptureSelection>(File.ReadAllBytes(nativePath));
var captureOwnerPath=Option("--capture-owner");
using var captureOwnerLock=captureOwnerPath is null?null:File.Open(captureOwnerPath,FileMode.Open,FileAccess.Read,FileShare.Read);
if(captureOwnerLock?.Length>4096)throw new InvalidDataException("Bounded private capture owner selection required");
var captureConfig=captureOwnerPath is null?null:OwnerJson.Read<CaptureOwnerConfig>(File.ReadAllBytes(captureOwnerPath));
native?.RequireDurableAdmission(owned is not null,captureConfig is not null);
native?.Validate();native?.RequireSameTarget(selection,owned);
bool ModeAllowed(string id,string mode)=>mode==NativeCaptureSelection.Raw||mode==NativeCaptureSelection.Square&&native?.Source(id) is not null;
string[] CaptureModes(string id)=>native?.Modes(id)??[NativeCaptureSelection.Raw];
var hostEndInput = Option("--host-end");
if (selection is not null && hostEndInput is null) throw new InvalidOperationException("Live host requires an explicit lifetime");
if(owned is not null&&hostEndInput is null)throw new InvalidDataException("Owned host requires explicit lifetime");
if (selection?.CaptureLifetime == MonitorLifetime.UntilStop && hostEndInput != MonitorLifetime.UntilStop) throw new InvalidOperationException("Until-stop capture requires until-stop host");
var hostEnd = hostEndInput is null or MonitorLifetime.UntilStop ? (DateTimeOffset?)null : MonitorLifetime.ParseUtc(hostEndInput);
if(hostEnd is not null && (hostEnd <= DateTimeOffset.UtcNow || selection is not null && hostEnd < selection.DeadlineUtc)) throw new InvalidOperationException("Host end must be future and at or after selected capture deadline");
var fixture = Option("--fixture");
if(Option("--hub-evidence") is not null && Option("--hub-owner") is not null)throw new InvalidDataException("Choose recorded evidence or selected owner projection");
using var projection = Option("--hub-owner") is {} projectionPath?new HubProjectionSource(projectionPath):null;
if(Option("--hub-session-owner") is not null && new[]{"--hub-owner","--hub-evidence","--hub-bridge"}.Any(key=>Option(key) is not null))
    throw new InvalidDataException("Read-only member sessions cannot select a pair-control source");
using var sessionProjection = Option("--hub-session-owner") is {} sessionPath?new HubSessionProjection(sessionPath):null;
var hub = new HubControl(Option("--hub-evidence"),projection);
using var hubBridge = Option("--hub-bridge") is { } bridgePath ? new HubOwnerBridge(bridgePath) : null;
if(owned is not null&&hubBridge?.LiveSupported!=true)throw new InvalidDataException("Owned display requires current authenticated pair owner");
var hubActions = new HubActionHandler(hub,hubBridge);
if (fixture is not null && (selection is not null || owned is not null || !Path.IsPathFullyQualified(fixture) || !File.Exists(fixture))) throw new InvalidOperationException("Fixture requires offline mode and an existing selected absolute file");
var busy = new HashSet<string>(); var sync = new object();
var fanouts = new Dictionary<string,CaptureFanout>();
var captureStops = new Dictionary<string,CancellationTokenSource>();
var captureStates = new Dictionary<string,string>();
var captureRequests = new Dictionary<string,(string Action,string Source,string Mode,object Response)>();
var captureModes = new Dictionary<string,string>();
var ownedRuns=new Dictionary<string,OwnedDisplayProducer>();
var nativeRuns=new Dictionary<string,NativeSquareCapture>();
using var ownerAdmission=new SemaphoreSlim(1,1);
if(captureConfig is not null&&(owned is null||hubBridge?.LiveSupported!=true||hubBridge.UsesSecret(captureConfig.Key)))throw new InvalidDataException("Separate capture audience key and selected owned producers required");
using var captureOwner=captureConfig is null?null:new CaptureOwnerService(captureConfig,owned!.Sources,s=>new SelectedCaptureInvocation(owned,s),async s=>{
    AppCaptureAdmission.Require(await hubBridge!.Current(),s);
},(id,fanout)=>{lock(sync)fanouts[id]=fanout;_=fanout.Metadata.Task.ContinueWith(task=>{if(task.Status==TaskStatus.RanToCompletion)lock(sync){if(fanouts.GetValueOrDefault(id)==fanout&&captureStates.GetValueOrDefault(id)=="requested")captureStates[id]="connected";}},TaskScheduler.Default);},(id,state,held)=>{lock(sync){captureStates[id]=state is "queued" or "running"?"requested":state;if(held)busy.Add(id);else busy.Remove(id);}},operatorStop.Token,()=>!operatorStop.IsCancellationRequested&&(hostEnd is null||DateTimeOffset.UtcNow<hostEnd),
    modeFactory:(source,mode)=>mode==NativeCaptureSelection.Square
        ?new NativeSquareCapture(native??throw new InvalidDataException("Native source not selected"),native!.Source(source.SourceId)??throw new InvalidDataException("Native target not selected"))
        :new SelectedCaptureInvocation(owned!,source),modeValidation:(source,mode)=>ModeAllowed(source.SourceId,mode));
var captureWire=captureConfig is null?null:new CaptureOwnerTransport(captureConfig,captureOwner!,"127.0.0.1:"+port);

_ = Task.Run(async () => { try { await Task.Delay(Timeout.Infinite, operatorStop.Token); } catch (OperationCanceledException) {} await MonitorLifetime.StopAfterRetirement(() => { lock(sync) return busy.Count == 0; }, app.Lifetime.StopApplication); });
app.UseWebSockets(); app.UseDefaultFiles(); app.UseStaticFiles();
if(captureWire is not null)app.MapMethods("/api/owner/{**capturePath}",["GET","POST"],captureWire.Handle);
bool Authorized(HttpContext c) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(c.Request.Query["token"].ToString()), Encoding.ASCII.GetBytes(token));
OwnerV2Routes.Map(app,hubBridge,Authorized,()=>origin,ownerAdmission,()=>{lock(sync)return busy.Count==0;});
app.MapGet("/api/sources", (HttpContext c) => Authorized(c) ? Results.Json(owned is not null?owned.Sources.Select(s=>(object)new{id=s.SourceId,s.Label,displayId=0,crop=(string?)null,s.DeviceId,sourceKind="owned-rmanvid1-v1",captureModes=CaptureModes(s.SourceId)}):selection?.Sources.Select(s => new {s.Id, s.Label, s.DisplayId, s.Crop,deviceId=s.Canonical,captureModes=CaptureModes(s.Id)}) ?? []) : Results.Unauthorized());
app.MapGet("/api/fixture", (HttpContext c) => Authorized(c) ? Results.Json(new { available = fixture is not null }) : Results.Unauthorized());
app.MapGet("/api/hub/session", (HttpContext c) => Authorized(c) ? Results.Json(HubControl.SessionStatus(sessionProjection)) : Results.Unauthorized());
app.MapGet("/api/captures", (HttpContext c) => {
    if(!Authorized(c))return Results.Unauthorized();
    (string Id,string State,bool Held,string? Mode,ScrcpyReadSnapshot? RawRead)[] snapshot;
    lock(sync)snapshot=captureStates.Select(pair=>(pair.Key,pair.Value,busy.Contains(pair.Key),captureModes.GetValueOrDefault(pair.Key),fanouts.GetValueOrDefault(pair.Key)?.RawReadProgress?.Snapshot())).ToArray();
    return Results.Json(snapshot.Select(row=>new{id=row.Id,state=row.State,retirementPending=row.Held,captureMode=captureOwner is null?row.Mode:((captureOwner.Current(row.Id).Body as CaptureCurrent)?.Operation is {} operation?operation.Mode??NativeCaptureSelection.Raw:null),rawRead=row.RawRead}).ToArray());
});
app.MapPost("/api/captures/{id}/{action}", async (HttpContext c,string id,string action) => {
    if(!Authorized(c)||c.Request.Headers.Origin!=origin)return Results.Unauthorized();
    if(c.Request.ContentLength is null or >4096 || action is not "start" and not "stop")return Results.BadRequest();
    CaptureOperatorRequest? request;
    try {request=await JsonSerializer.DeserializeAsync<CaptureOperatorRequest>(c.Request.Body,HubControl.Json);}
    catch(JsonException){return Results.BadRequest();}
    if(request is null || !Guid.TryParseExact(request.RequestId,"D",out _))return Results.BadRequest();
    var mode=request.CaptureMode??NativeCaptureSelection.Raw;
    if(!NativeCaptureSelection.Mode(mode)||action=="stop"&&request.CaptureMode is not null||action=="start"&&!ModeAllowed(id,mode))return Results.BadRequest(new{error="Capture mode is not configured for this source"});
    if(captureOwner is not null){
        var selectedSource=owned!.Sources.SingleOrDefault(s=>s.SourceId==id);if(selectedSource is null)return Results.NotFound();
        try{
            var original=captureOwner.Original(request.RequestId);
            if(original.Status==200){var result=(CaptureOwnerResult)original.Body;return result.Action==action&&result.SourceId==id&&(action=="stop"||(result.Mode??NativeCaptureSelection.Raw)==mode)?Results.Json(result,OwnerJson.Options):Results.Conflict();}
            CaptureInvocation? target=null;
            if(action=="stop"){var snapshot=(CaptureCurrent)captureOwner.Current(id).Body;if(snapshot.Operation is not {} operation)return Results.Conflict(new{error="No exact current invocation to stop"});target=new(operation.InvocationId,operation.Generation);}
            var reply=captureOwner.Submit(new("questms.capture.action.v4",request.RequestId,action,id,captureConfig!.OwnerEpoch,selectedSource.OwnerBindingRef.Sha256,target,action=="start"?mode:null));return Results.Json(reply.Body,OwnerJson.Options,statusCode:reply.Status);
        }catch(InvalidDataException){return Results.BadRequest();}catch(IOException){return Results.Json(new{error="Original capture unresolved"},statusCode:503);}
    }
    await ownerAdmission.WaitAsync();try{
    var source=selection?.Sources.SingleOrDefault(s=>s.Id==id);
    var ownedSource=owned?.Sources.SingleOrDefault(s=>s.SourceId==id);
    if(source is null&&ownedSource is null)return Results.NotFound();
    if(action=="start"&&ownedSource is not null){try{AppCaptureAdmission.Require(await hubBridge!.Current(),ownedSource);}catch{return Results.Json(new{state="owner-unavailable"},statusCode:503);}}
    lock(sync) {
        if(captureRequests.TryGetValue(request.RequestId,out var prior))return prior.Action==action&&prior.Source==id&&prior.Mode==mode?Results.Json(prior.Response):Results.Conflict();
        if(captureRequests.Count>=256)return Results.Conflict(new {state="request-capacity"});
        if(operatorStop.IsCancellationRequested||hostEnd is not null&&DateTimeOffset.UtcNow>=hostEnd)return Results.StatusCode(410);
        object response;
        if(action=="stop") {
            if(captureStops.TryGetValue(id,out var stop)){stop.Cancel();captureStates[id]="stopping";}
            response=new {state=captureStates.GetValueOrDefault(id)=="unknown"?"unknown":busy.Contains(id)?"stopping":"retired",source=id,retirementPending=busy.Contains(id)};
        } else if(busy.Contains(id)){if(captureModes.GetValueOrDefault(id)!=mode)return Results.Conflict(new{error="Stop and retire the current capture before changing source mode"});response=new {state=captureStates.GetValueOrDefault(id,"running"),source=id,newCapture=false};}
        else {
            busy.Add(id);captureModes[id]=mode;var fanout=new CaptureFanout();fanouts[id]=fanout;
            _=fanout.Metadata.Task.ContinueWith(task=>{if(task.Status==TaskStatus.RanToCompletion)lock(sync){if(fanouts.GetValueOrDefault(id)==fanout&&captureStates.GetValueOrDefault(id)=="requested")captureStates[id]="connected";}},TaskScheduler.Default);
            var stop=CancellationTokenSource.CreateLinkedTokenSource(operatorStop.Token);captureStops[id]=stop;captureStates[id]="requested";
            var nativeRun=mode==NativeCaptureSelection.Square?new NativeSquareCapture(native!,native!.Source(id)!):null;if(nativeRun is not null)nativeRuns[id]=nativeRun;
            var ownedRun=ownedSource is null||nativeRun is not null?null:new OwnedDisplayProducer(owned!,ownedSource);if(ownedRun is not null)ownedRuns[id]=ownedRun;
            _=Task.Run(async()=>{
                Exception? failure=null;
                try {if(nativeRun is not null)await nativeRun.Run(fanout,stop.Token);else if(ownedRun is not null)await ownedRun.Run(fanout,stop.Token);else await Capture(null,selection!,source!,stop.Token,fanout);}
                catch(Exception e){failure=e;app.Logger.LogError(e,"Owned capture ended with failure; no replay");}
                finally {fanout.Complete(failure);lock(sync){var held=ownedRun?.Started==true&&!ownedRun.RetirementProven||nativeRun?.RetirementPending==true;if(!held)busy.Remove(id);captureStops.Remove(id);captureStates[id]=held?"unknown":failure is null?"retired":"failed";}stop.Dispose();}
            });
            response=new {state="requested",source=id,newCapture=true};
        }
        captureRequests.Add(request.RequestId,(action,id,mode,response));return Results.Json(response);
    }
    }finally{ownerAdmission.Release();}
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
app.MapGet("/api/hub/capabilities", (HttpContext c) => Authorized(c) ? Results.Json(new { bridgeSelected = hubBridge is not null, protocolVersion=hubBridge?.ProtocolVersion??1, liveSupported=hubBridge?.LiveSupported==true, readOnlySession = sessionProjection is not null, direction = "both", actions=sessionProjection is not null?Array.Empty<string>():hubBridge?.Actions??["establish","disconnect"],execution = "Owner must authenticate live authority; selection alone proves no readiness" }) : Results.Unauthorized());
app.MapGet("/api/hub/current",async(HttpContext c)=>{if(!Authorized(c))return Results.Unauthorized();try{return Results.Json(await (hubBridge??throw new InvalidDataException()).Current(),OwnerJson.Options);}catch{return Results.Json(new{error="Current owner unavailable; do not infer stopped"},statusCode:503);}});
app.MapGet("/api/hub/devices",async(HttpContext c)=>{if(!Authorized(c))return Results.Unauthorized();try{return Results.Json(await (hubBridge??throw new InvalidDataException()).Devices(),OwnerJson.Options);}catch{return Results.Json(new{error="Device inventory unavailable"},statusCode:503);}});
app.MapPost("/api/hub/selection",async(HttpContext c)=>{
    if(!Authorized(c)||c.Request.Headers.Origin!=origin)return Results.Unauthorized();if(c.Request.ContentLength is null or >4096)return Results.BadRequest();
    await ownerAdmission.WaitAsync();try{
    lock(sync)if(busy.Count!=0)return Results.Conflict(new{error="Display owners remain active or unresolved"});
    try{using var body=new MemoryStream();await c.Request.Body.CopyToAsync(body);if(body.Length>4096)return Results.BadRequest();return Results.Json(await (hubBridge??throw new InvalidDataException()).Select(OwnerJson.Read<OwnerSelectionRequest>(body.ToArray())),OwnerJson.Options);}catch{return Results.Json(new{error="Selection unresolved or denied; read current owner"},statusCode:409);}
    }finally{ownerAdmission.Release();}
});
app.MapGet("/api/hub/actions/{requestId}", async (HttpContext c,string requestId) => {
    if (!Authorized(c)) return Results.Unauthorized();
    if (hubBridge is null) return Results.Json(new { error = "App-owner executor unavailable" },statusCode:503);
    try { return Results.Json(await hubBridge.Status(requestId)); }
    catch (InvalidDataException e) { return Results.BadRequest(new { error=e.Message }); }
    catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException) { return Results.Json(new { error="Owner readback unavailable; original action remains owned" },statusCode:502); }
});
app.MapPost("/api/hub/actions", async (HttpContext c) => {
    if (!Authorized(c) || c.Request.Headers.Origin != origin) return Results.Unauthorized();
    if(sessionProjection is not null)return Results.Json(new {error="Read-only member session; pair actions unavailable"},statusCode:409);
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
Console.WriteLine(selection is null ? "OFFLINE: file/window fallback only; no ADB/device operations" : "Explicit selected device capture; lifecycle EOF only, no input events, audio, wake or retries");
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
    var rawRead = new ScrcpyReadProgress(); fanout.RawReadProgress = rawRead;
    evidence.Event("intent", new { source, remote, config.DeadlineUtc, config.DaemonPid, config.DaemonStartUtcTicks, config.AdbSha256, config.ServerSha256, config.CaptureLifetime });
    Process? child = null; Task<DrainResult>? output = null, error = null;
    ScrcpyConnection? tcp = null; var forwarded = false; var pushed = false;
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
        var reader = new ScrcpyVideoReader(tcp.GetStream(), rawRead); var size = await reader.Header(deadline.Token, dummyAlreadyRead: true);
        evidence.SafeEvent("video-ready", new { size.Width, size.Height, rawRead = rawRead.Snapshot() });
        var metadata=new {kind="video",size.Width,size.Height,codec="h264",captureMode=NativeCaptureSelection.Raw,frameLayout="stereo-sbs"};fanout.SetMetadata(metadata);
        if(socket is not null)await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata)), WebSocketMessageType.Text, true, deadline.Token);
        while (socket is null || socket.State == WebSocketState.Open)
        {
            var packet = await reader.Packet(deadline.Token); if (packet is null) break;
            fanout.Publish(packet);
            if(socket is not null)await socket.SendAsync(packet, WebSocketMessageType.Binary, true, deadline.Token);
        }
    }
    catch (OperationCanceledException) when (MonitorLifetime.OperatorStopWithinScope(operatorStop.IsCancellationRequested, config.DeadlineUtc, DateTimeOffset.UtcNow) && failure is null) { evidence.SafeEvent("operator-stop-observed", new { rawRead = rawRead.Snapshot() }); }
    catch (OperationCanceledException) when (browserClosed && failure is null) { evidence.SafeEvent("browser-close-observed", new { rawRead = rawRead.Snapshot() }); }
    catch (Exception e) { failure ??= e; evidence.SafeEvent("capture-failed", new { type = e.GetType().Name, e.Message, rawRead = rawRead.Snapshot() }); }
    finally
    {
        tcp?.Dispose();
        deadline.Cancel();
        receiveStop.Cancel();
        if (receive is not null) await receive;
        if (monitor is not null) await monitor;
        if (child is not null)
        {
            if (!child.HasExited) evidence.SafeEvent("HELD", new { pid = child.Id, reason = "Awaiting actual server child exit; source locks retained; no kill/replay", rawRead = rawRead.Snapshot() });
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
        evidence.SafeEvent("terminal", new { success = failure is null, failure = failure?.Message, evidence_errors = evidence.Errors, rawRead = rawRead.Snapshot() });
        if (evidence.Errors.Count > 0) failure ??= new IOException("Stage evidence retention failed");
        if (socket?.State is WebSocketState.Open or WebSocketState.CloseReceived) await socket.CloseOutputAsync(failure is null ? WebSocketCloseStatus.NormalClosure : WebSocketCloseStatus.InternalServerError, failure is null ? "Selected capture ended" : "Capture failed; inspect retained host evidence", CancellationToken.None);
    }
    if (failure is not null) throw failure;
}

public sealed record CaptureOperatorRequest(string RequestId,string? CaptureMode=null);
