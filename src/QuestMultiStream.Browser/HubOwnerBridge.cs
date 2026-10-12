using System.Net.Http.Headers;
using System.Text.Json;

namespace QuestMultiStream.Browser;

public sealed record HubBridgeSelection(string Endpoint, string Token, string Generation, string Session,string[]? Actions=null);
public sealed record HubOwnerReadback(string State, string RequestId, string Generation, string Session,
    string Receipt, bool ActualEffectsCompleted,string? ActualNativeSession=null);

// HTTP only transports closed owner requests. The selected owner retains guards,
// signature checks, admission/renewal single-flight and actual subprocess ownership.
public sealed class HubOwnerBridge : IDisposable
{
    private readonly HubBridgeSelection selection;
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
    private readonly Dictionary<string, Task<HubOwnerReadback>> requests = [];
    private readonly Dictionary<string, HubAction> inputs = [];
    private readonly object sync = new();
    public string[] Actions=>selection.Actions??["establish","disconnect"];
    public HubOwnerBridge(string path) {
        if (!Path.IsPathFullyQualified(path) || new FileInfo(path).Length > 4096) throw new InvalidDataException("Bounded selected bridge configuration required");
        selection = JsonSerializer.Deserialize<HubBridgeSelection>(File.ReadAllText(path), HubControl.Json) ?? throw new InvalidDataException("Missing bridge selection");
        var uri = new Uri(selection.Endpoint);
        if (uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.AbsolutePath != "/operator" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0 ||
            string.IsNullOrWhiteSpace(selection.Session) || selection.Token.Length < 32 || string.IsNullOrWhiteSpace(selection.Generation)) throw new InvalidDataException("Explicit authenticated loopback owner endpoint required");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", selection.Token);
        if(Actions.Length is <1 or >2||Actions.Distinct().Count()!=Actions.Length||Actions.Any(action=>action is not "establish" and not "disconnect"))throw new InvalidDataException("Closed selected owner action inventory required");
    }
    public Task<HubOwnerReadback> Execute(HubAction request) {
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
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var message=new HttpRequestMessage(HttpMethod.Post,selection.Endpoint) {Content=JsonContent.Create(new { schema="questms.owner.action.v1", request.Action, request.Direction, request.Generation, request.RequestId, session=selection.Session },options:HubControl.Json)};
        using var response = await client.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        return await ReadResponse(response,request.RequestId,deadline.Token);
    }
    public async Task<HubOwnerReadback> Status(string requestId) {
        if (!Guid.TryParseExact(requestId,"D",out _)) throw new InvalidDataException("Canonical owner request ID required");
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var message=new HttpRequestMessage(HttpMethod.Get,selection.Endpoint+"/"+requestId);
        using var response = await client.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        return await ReadResponse(response,requestId,deadline.Token);
    }
    private async Task<HubOwnerReadback> ReadResponse(HttpResponseMessage response,string requestId,CancellationToken deadline) {
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 65536) throw new InvalidDataException("Owner response exceeds bound");
        using var stream = await response.Content.ReadAsStreamAsync(deadline);
        var buffer = new byte[65537]; var length = 0;
        while (length < buffer.Length) { var read = await stream.ReadAsync(buffer.AsMemory(length),deadline); if (read == 0) break; length += read; }
        if (length > 65536) throw new InvalidDataException("Owner response exceeds bound");
        var bytes = buffer.AsSpan(0,length).ToArray();
        var result = JsonSerializer.Deserialize<HubOwnerReadback>(bytes,HubControl.Json) ?? throw new InvalidDataException("Missing owner readback");
        if (result.RequestId != requestId || result.Generation != selection.Generation || result.Session != selection.Session || string.IsNullOrWhiteSpace(result.Receipt) ||
            result.State is not "queued" and not "running" and not "effective" and not "failed" and not "unknown" || result.ActualEffectsCompleted && result.State != "effective") throw new InvalidDataException("Owner readback identity/state differs");
        return result;
    }
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
