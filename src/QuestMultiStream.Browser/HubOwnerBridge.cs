using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;

namespace QuestMultiStream.Browser;

public sealed record HubBridgeSelection(string Endpoint, string Token, string Generation, string Session,string[]? Actions=null,string? OwnerEpoch=null,int ProtocolVersion=1);
public sealed record HubOwnerReadback(string State, string RequestId, string Generation, string Session,
    string Receipt, bool ActualEffectsCompleted,string? ActualNativeSession=null);

// HTTP only transports closed owner requests. The selected owner retains guards,
// signature checks, admission/renewal single-flight and actual subprocess ownership.
public sealed partial class HubOwnerBridge : IDisposable
{
    private HubBridgeSelection selection;
    private readonly HttpClient client;
    private readonly Dictionary<string, Task<HubOwnerReadback>> requests = [];
    private readonly Dictionary<string, HubAction> inputs = [];
    private readonly object sync = new();
    private long lastRevision=-1;
    public string[] Actions=>selection.Actions??["establish","disconnect"];
    public bool LiveSupported=>selection.OwnerEpoch is not null;
    public int ProtocolVersion=>selection.ProtocolVersion;
    internal bool UsesSecret(string key)=>selection.Token==key;
    public HubOwnerBridge(string path):this(path,null){}
    internal HubOwnerBridge(string path,HttpMessageHandler? transport) {
        client=new HttpClient(transport??new HttpClientHandler { AllowAutoRedirect = false,UseProxy=false }){Timeout=TimeSpan.FromSeconds(10)};
        if (!Path.IsPathFullyQualified(path) || new FileInfo(path).Length > 4096) throw new InvalidDataException("Bounded selected bridge configuration required");
        selection = JsonSerializer.Deserialize<HubBridgeSelection>(File.ReadAllText(path), HubControl.Json) ?? throw new InvalidDataException("Missing bridge selection");
        var uri = new Uri(selection.Endpoint);
        if (uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.AbsolutePath != "/operator" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0 ||
            string.IsNullOrWhiteSpace(selection.Session) || selection.Token.Length < 32 || string.IsNullOrWhiteSpace(selection.Generation)) throw new InvalidDataException("Explicit authenticated loopback owner endpoint required");
        // Live mode authenticates exact bytes without disclosing its MAC key to
        // whichever process currently occupies the selected loopback port.
        // Bearer remains exclusively the older, non-live bridge contract.
        if(selection.OwnerEpoch is null)client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", selection.Token);
        if(Actions.Length is <1 or >2||Actions.Distinct().Count()!=Actions.Length||Actions.Any(action=>action is not "establish" and not "disconnect"))throw new InvalidDataException("Closed selected owner action inventory required");
        if(selection.OwnerEpoch is {} epoch&&(!System.Text.RegularExpressions.Regex.IsMatch(epoch,"^[a-zA-Z0-9._-]{1,128}$")||!System.Text.RegularExpressions.Regex.IsMatch(selection.Token,"^[a-zA-Z0-9_-]{43,256}$")))throw new InvalidDataException("Live owner epoch and strong token encoding required");
        if(selection.ProtocolVersion is not (1 or 2)||selection.ProtocolVersion==2&&!LiveSupported)throw new InvalidDataException("Explicit supported authenticated protocol required");
    }
    public Task<HubOwnerReadback> Execute(HubAction request) {
        if(ProtocolVersion!=1)throw new InvalidDataException("v1 action requires selected v1 protocol");
        if (!request.Execute || !Actions.Contains(request.Action) || request.Direction != "both" ||
            request.Generation != selection.Generation || !Guid.TryParseExact(request.RequestId,"D",out _)) throw new InvalidDataException("Closed bilateral owner action/selection required");
        lock (sync) {
            if (requests.TryGetValue(request.RequestId,out var retained)) {
                if (inputs[request.RequestId] != request) throw new InvalidDataException("Request replay differs");
                return retained; // Failure/timeout stays retained; never replay a possibly effected request.
            }
            if (requests.Count >= 256) throw new InvalidDataException("Owner request capacity reached");
            inputs.Add(request.RequestId,request);
            var task = Send(request); requests.Add(request.RequestId,task); return task;
        }
    }
    private async Task<HubOwnerReadback> Send(HubAction request) {
        var body=JsonSerializer.SerializeToUtf8Bytes(new { schema="questms.owner.action.v1", request.Action, request.Direction, request.Generation, request.RequestId, session=selection.Session },HubControl.Json);
        return ValidateReadback(await Exchange<HubOwnerReadback>(HttpMethod.Post,"",body),request.RequestId);
    }
    public async Task<HubOwnerReadback> Status(string requestId) {
        if(ProtocolVersion!=1)throw new InvalidDataException("v1 result requires selected v1 protocol");
        if (!Guid.TryParseExact(requestId,"D",out _)) throw new InvalidDataException("Canonical owner request ID required");
        return ValidateReadback(await Exchange<HubOwnerReadback>(HttpMethod.Get,"/"+requestId,[]),requestId);
    }
    private HubOwnerReadback ValidateReadback(HubOwnerReadback result,string requestId) {
        if (result.RequestId != requestId || result.Generation != selection.Generation || result.Session != selection.Session || string.IsNullOrWhiteSpace(result.Receipt) ||
            result.State is not "queued" and not "running" and not "effective" and not "failed" and not "unknown" || result.ActualEffectsCompleted && result.State != "effective" || LiveSupported&&result.State=="effective"&&!result.ActualEffectsCompleted) throw new InvalidDataException("Owner readback identity/state differs");
        return result;
    }
    internal static string Mac(string token,string domain,string first,string path,string epoch,string nonce,byte[] bytes)=>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(token),Encoding.UTF8.GetBytes(domain+"\n"+first+"\n"+path+"\n"+epoch+"\n"+nonce+"\n"+Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()))).ToLowerInvariant();
    private async Task<T> Exchange<T>(HttpMethod method,string suffix,byte[] body) {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var selected=selection;var uri=new Uri(selected.Endpoint+suffix);var nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        using var message=new HttpRequestMessage(method,uri);
        if(method==HttpMethod.Post){message.Content=new ByteArrayContent(body);message.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json");}
        if(selected.OwnerEpoch is {} epoch){message.Headers.Add("X-Quest-Owner-Epoch",epoch);message.Headers.Add("X-Quest-Nonce",nonce);message.Headers.Add("X-Quest-Mac",Mac(selected.Token,"QMS-OWNER-REQUEST-1",method.Method,uri.AbsolutePath,epoch,nonce,body));}
        using var response=await client.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 65536) throw new InvalidDataException("Owner response exceeds bound");
        using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        var buffer = new byte[65537]; var length = 0;
        while (length < buffer.Length) { var read = await stream.ReadAsync(buffer.AsMemory(length),deadline.Token); if (read == 0) break; length += read; }
        if (length > 65536) throw new InvalidDataException("Owner response exceeds bound");
        var bytes = buffer.AsSpan(0,length).ToArray();
        if(selected.OwnerEpoch is {} expectedEpoch){
            string Header(string name){if(!response.Headers.TryGetValues(name,out var values)||values.Count()!=1)throw new InvalidDataException("Missing/duplicate authenticated owner header");return values.Single();}
            if(Header("X-Quest-Owner-Epoch")!=expectedEpoch||Header("X-Quest-Nonce")!=nonce)throw new InvalidDataException("Owner epoch/nonce changed");
            byte[] mac;try{mac=Convert.FromHexString(Header("X-Quest-Mac"));}catch(FormatException){throw new InvalidDataException("Malformed owner MAC");}
            var expected=Convert.FromHexString(Mac(selected.Token,"QMS-OWNER-REPLY-1",((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture),uri.AbsolutePath,expectedEpoch,nonce,bytes));
            if(!CryptographicOperations.FixedTimeEquals(mac,expected))throw new InvalidDataException("Owner response authentication failed");
        }
        return OwnerJson.Read<T>(bytes);
    }
    public async Task<OwnerLiveStatus> Current() {
        if(ProtocolVersion!=1)throw new InvalidDataException("v1 status requires selected v1 protocol");
        if(!LiveSupported)throw new InvalidDataException("Live status not selected");
        var status=await Exchange<OwnerLiveStatus>(HttpMethod.Get,"/status",[]);status.Validate(selection.OwnerEpoch!,DateTimeOffset.UtcNow);
        Bind(status);
        return status;
    }
    public async Task<OwnerDevices> Devices() {
        if(ProtocolVersion!=1)throw new InvalidDataException("v1 inventory requires selected v1 protocol");
        if(!LiveSupported)throw new InvalidDataException("Live inventory not selected");
        var result=await Exchange<OwnerDevices>(HttpMethod.Get,"/devices",[]);result.Validate(selection.OwnerEpoch!,DateTimeOffset.UtcNow);return result;
    }
    public async Task<OwnerLiveStatus> Select(OwnerSelectionRequest request) {
        if(ProtocolVersion!=1)throw new InvalidDataException("v1 selection requires selected v1 protocol");
        if(!LiveSupported||request.Schema!="questms.owner.selection.v1"||request.OwnerEpoch!=selection.OwnerEpoch||!Guid.TryParseExact(request.RequestId,"D",out _)||request.ExpectedRevision<0||request.Members is null||request.Members.Length!=2||request.Members.Any(m=>m is null)||!request.Members.Select(m=>m.Role).Order().SequenceEqual(new[]{"Q","W"})||request.Members.Any(m=>string.IsNullOrWhiteSpace(m.DeviceId))||request.Members.Select(m=>m.DeviceId).Distinct().Count()!=2)throw new InvalidDataException("Closed distinct pair selection required");
        var result=await Exchange<OwnerLiveStatus>(HttpMethod.Post,"/selection",JsonSerializer.SerializeToUtf8Bytes(request,OwnerJson.Options));result.Validate(selection.OwnerEpoch!,DateTimeOffset.UtcNow);
        if(!result.Selection.Members.OrderBy(m=>m.Role).SequenceEqual(request.Members.OrderBy(m=>m.Role)))throw new InvalidDataException("Selected pair differs");
        Bind(result);return result;
    }
    private void Bind(OwnerLiveStatus status){lock(sync){if(status.Revision<lastRevision)throw new InvalidDataException("Owner revision moved backwards");lastRevision=status.Revision;if(status.Selection.Generation is {} generation&&status.Selection.Session is {} session)selection=selection with{Generation=generation,Session=session};}}
    public void Dispose() => client.Dispose();

    internal static async Task SelfTest() {
        // Real HTTP transport; the owner and all native effects are modeled.
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(System.Net.IPAddress.Loopback,0));
        await using var app = builder.Build();
        var token = new string('t',32); var posts = 0;
        app.MapPost("/operator", async (HttpContext c) => {
            if (c.Request.Headers.Authorization != "Bearer "+token) return Results.Unauthorized();
            var request = await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body);
            Interlocked.Increment(ref posts);
            return Results.Json(new HubOwnerReadback("queued",request.GetProperty("requestId").GetString()!,"model-generation","model-session","Modeled durable intent; no native effects",false));
        });
        app.MapGet("/operator/{id}",(string id) => Results.Json(new HubOwnerReadback("running",id,"model-generation","model-session","Modeled worker readback",false)));
        await app.StartAsync();
        var path = Path.Combine(Path.GetTempPath(),"questms-bridge-"+Guid.NewGuid().ToString("N")+".json");
        try {
            File.WriteAllText(path,JsonSerializer.Serialize(new HubBridgeSelection(app.Urls.Single()+"/operator",token,"model-generation","model-session"),HubControl.Json));
            using var bridge = new HubOwnerBridge(path);
            var request = new HubAction("establish","both","model-generation",Guid.NewGuid().ToString("D"),true);
            var first = await bridge.Execute(request); var replay = await bridge.Execute(request);
            if (first != replay || posts != 1 || first.State != "queued" || first.ActualEffectsCompleted) throw new Exception("Real owner transport replay/queued state failed");
            var status = await bridge.Status(request.RequestId);
            if (status.State != "running" || status.ActualEffectsCompleted || posts != 1) throw new Exception("Owner status issued an effect or misclassified progress");
            try { await bridge.Execute(request with {Action="disconnect"}); throw new Exception("Conflicting owner replay accepted"); } catch (InvalidDataException) { }
            try { await bridge.Execute(request with {Direction="Q→W",RequestId=Guid.NewGuid().ToString("D")}); throw new Exception("Unilateral owner effect accepted"); } catch (InvalidDataException) { }
        } finally { await app.StopAsync(); File.Delete(path); }
        Console.WriteLine("PASS real isolated HTTP owner POST/status, once-only replay, closed direction and modeled queued/running truth; no device operations");
    }
}
