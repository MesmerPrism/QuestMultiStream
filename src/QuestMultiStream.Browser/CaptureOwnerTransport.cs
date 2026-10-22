using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuestMultiStream.Browser;

internal sealed record CaptureWireReply(int Status,Dictionary<string,string> Headers,byte[] Body);

// A separate server audience. The selected secret is a signing key, never a
// bearer. Timestamp-fenced capture nonces have bounded recent RAM, no poll cap.
internal sealed class CaptureOwnerTransport {
    readonly CaptureOwnerConfig selected;readonly CaptureOwnerService service;readonly string host;
    readonly Func<DateTimeOffset> clock;readonly object nonceLock=new();readonly Dictionary<string,long> recent=[];long lastClock;
    internal const long MaximumAgeMs=30000,MaximumFutureMs=2000;internal const int MaximumRecent=8192;
    public const string RequestDomain="QMS-CAPTURE-REQUEST-1",ReplyDomain="QMS-CAPTURE-REPLY-1";
    public CaptureOwnerTransport(CaptureOwnerConfig selected,CaptureOwnerService service,string host,Func<DateTimeOffset>? clock=null){
        this.selected=selected;this.service=service;this.host=host;this.clock=clock??(()=>DateTimeOffset.UtcNow);
        // A live epoch is a single process incarnation. Reusing the config after
        // restart cannot clear a still-fresh replay cache and accept old nonces.
        CaptureOwnerService.Create(Path.Combine(selected.JournalDirectory,"AUTH_EPOCH_USED.json"),JsonSerializer.SerializeToUtf8Bytes(new{selected.OwnerEpoch},OwnerJson.Options));
    }
    internal static string NewNonce(DateTimeOffset now)=>now.ToUnixTimeMilliseconds().ToString("x16")+Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    public CaptureWireReply Exchange(string method,string path,IReadOnlyList<KeyValuePair<string,string>> headers,byte[] body,IPAddress? peer){
        var empty=new CaptureWireReply(401,[],[]);
        if(peer is null||!peer.Equals(IPAddress.Loopback)||path.Length is <1 or >128||path.Any(c=>c is <'!' or >'~')||headers.Count>64||headers.Sum(h=>h.Key.Length+h.Value.Length)>8192||body.Length>4096||method is not ("GET" or "POST"))return empty;
        if(headers.Any(h=>!Regex.IsMatch(h.Key,"^[!#$%&'*+.^_`|~0-9a-zA-Z-]+$")||h.Value.Any(c=>c is <' ' or >'~')))return empty;
        string? One(string name){var values=headers.Where(h=>h.Key.Equals(name,StringComparison.OrdinalIgnoreCase)).Select(h=>h.Value).ToArray();return values.Length==1?values[0]:null;}
        if(One("Host")!=host||headers.Any(h=>h.Key.Equals("Origin",StringComparison.OrdinalIgnoreCase)||h.Key.Equals("Authorization",StringComparison.OrdinalIgnoreCase)||h.Key.Equals("Cookie",StringComparison.OrdinalIgnoreCase)))return empty;
        var epoch=One("X-Quest-Owner-Epoch");var nonce=One("X-Quest-Nonce");var supplied=One("X-Quest-Mac");
        if(epoch!=selected.OwnerEpoch||nonce is null||!Regex.IsMatch(nonce,"^[0-9a-f]{32}$")||supplied is null||!Regex.IsMatch(supplied,"^[0-9a-f]{64}$"))return empty;
        var expected=HubOwnerBridge.Mac(selected.Key,RequestDomain,method,path,epoch,nonce,body);
        if(!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(supplied),Convert.FromHexString(expected)))return empty;
        CaptureWireReply Reply(int status,object value){var bytes=JsonSerializer.SerializeToUtf8Bytes(value,OwnerJson.Options);if(bytes.Length>65536){status=503;bytes="{\"error\":\"Capture response exceeds bound\"}"u8.ToArray();}return new(status,new(){["X-Quest-Owner-Epoch"]=epoch,["X-Quest-Nonce"]=nonce,["X-Quest-Mac"]=HubOwnerBridge.Mac(selected.Key,ReplyDomain,status.ToString(System.Globalization.CultureInfo.InvariantCulture),path,epoch,nonce,bytes)},bytes);}
        lock(nonceLock){
            var now=clock().ToUnixTimeMilliseconds();if(now<lastClock)return Reply(503,new{error="Capture authentication clock regressed; no dispatch"});lastClock=now;
            var stamp=ulong.Parse(nonce[..16],System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture);
            if(stamp>(ulong)(now+MaximumFutureMs)||stamp<(ulong)(now-MaximumAgeMs))return Reply(401,new{error="Capture nonce timestamp outside authenticated window"});
            foreach(var expired in recent.Where(p=>p.Value<now-MaximumAgeMs).Select(p=>p.Key).ToArray())recent.Remove(expired);
            if(recent.ContainsKey(nonce))return Reply(409,new{error="Capture transport replay"});
            if(recent.Count>=MaximumRecent)return Reply(503,new{error="Capture authentication recent window capacity; no dispatch"});
            recent.Add(nonce,(long)stamp); // Reserve before parsing/handler, under one lock.
        }
        try{
            CaptureReply result;
            if(method=="POST"&&path=="/api/owner/capture-actions"){
                if(One("Content-Type")!="application/json")return Reply(400,new{error="Exact bounded JSON required"});
                using var parsed=JsonDocument.Parse(body);
                if(parsed.RootElement.TryGetProperty("schema",out var declared)&&declared.GetString()=="questms.capture.action.v2"&&parsed.RootElement.TryGetProperty("mode",out _))throw new InvalidDataException("Legacy raw action has no mode member");
                result=parsed.RootElement.TryGetProperty("schema",out var schema)&&schema.GetString()=="questms.capture.action.v3"?service.SubmitOriginalStop(OwnerJson.Read<CaptureOriginalStopAction>(body)):service.Submit(OwnerJson.Read<CaptureOwnerAction>(body));
            }else if(method=="GET"&&body.Length==0){
                if(path=="/api/owner/status")result=new(200,service.Status());
                else if(path.StartsWith("/api/owner/capture-actions/",StringComparison.Ordinal)&&CaptureOwnerService.CanonicalId(path[27..]))result=service.Original(path[27..]);
                else if(path.StartsWith("/api/owner/captures/",StringComparison.Ordinal)&&Regex.IsMatch(path[20..],"^[a-zA-Z0-9_-]{1,64}$"))result=service.Current(path[20..]);
                else return Reply(400,new{error="Closed capture observation route required"});
            }else return Reply(400,new{error="Closed capture method/path/body required"});
            return Reply(result.Status,result.Body);
        }catch(Exception error)when(error is JsonException or InvalidDataException){return Reply(400,new{error="Closed exact capture action required"});}
        catch(Exception){return Reply(503,new{error="Original capture unresolved; reconcile original ID"});}
    }
    public async Task Handle(HttpContext context){
        // Bounded acquisition is separate from authentication. Unknown lengths,
        // transfer encoding, queries and browser-origin requests are not admitted.
        if(context.Request.ContentLength is >4096||context.Request.ContentLength is null&&context.Request.Method=="POST"||context.Request.Headers.ContainsKey("Transfer-Encoding")||context.Request.QueryString.HasValue){context.Response.StatusCode=401;return;}
        using var body=new MemoryStream();var buffer=new byte[4097];var total=0;
        while(total<buffer.Length){var count=await context.Request.Body.ReadAsync(buffer.AsMemory(total),context.RequestAborted);if(count==0)break;total+=count;}
        if(total>4096){context.Response.StatusCode=401;return;}
        var headers=context.Request.Headers.SelectMany(h=>h.Value.Select(v=>new KeyValuePair<string,string>(h.Key,v??""))).ToArray();
        var reply=Exchange(context.Request.Method,context.Request.Path.Value??"",headers,buffer.AsSpan(0,total).ToArray(),context.Connection.RemoteIpAddress);
        context.Response.StatusCode=reply.Status;foreach(var header in reply.Headers)context.Response.Headers[header.Key]=header.Value;
        context.Response.ContentType="application/json";await context.Response.Body.WriteAsync(reply.Body,context.RequestAborted);
    }
}
