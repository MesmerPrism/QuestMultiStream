using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

namespace QuestMultiStream.Browser;

internal static class BrowserGatewayTests
{
    const string Token="0123456789abcdef0123456789abcdef0123456789abcdef";
    const string Local="http://127.0.0.1:43118";
    const string Public="https://controls.example.test";
    static void Check(bool condition,string reason){if(!condition)throw new Exception(reason);}
    internal static async Task Run()
    {
        var selection=new BrowserGatewaySelection("192.168.20.10",44319,"https://controls.example.test/bilateral-control/");
        selection.Validate();
        (selection with{ListenAddress="127.0.0.2"}).Validate();
        foreach(var invalid in new[]{selection with{ListenAddress="8.8.8.8"},selection with{ListenAddress="0.0.0.0"},selection with{ListenAddress="127.1"},selection with{ListenAddress="::1"},selection with{Port=443},selection with{PublicAppUrl="http://controls.example.test/app/"},selection with{PublicAppUrl="https://u:p@controls.example.test/app/"},selection with{PublicAppUrl="https://controls.example.test/app/?token=x"},selection with{PublicAppUrl="https://controls.example.test/app/#token=x"}})
        {
            var rejected=false;try{invalid.Validate();}catch(InvalidDataException){rejected=true;}
            if(!rejected)throw new Exception("Gateway admitted an unsafe listener or public application URL: "+invalid);
        }
        Console.WriteLine("PASS strict optional gateway selection");
        // Parser regressions must fail before any listener can be configured.
        var selectionFile=Path.Combine(Path.GetTempPath(),"qms-gateway-selection-"+Guid.NewGuid().ToString("N")+".json");
        try{
            File.WriteAllText(selectionFile,"{\"listenAddress\":\"127.0.0.1\",\"port\":44319,\"publicAppUrl\":\"https://controls.example.test/bilateral-control/\"}");
            Check(BrowserGatewayEndpoint.Load(selectionFile)?.Selection.Origin==Public,"Public app path became an Origin boundary");
            foreach(var json in new[]{"{\"listenAddress\":\"127.0.0.1\",\"port\":44319,\"publicAppUrl\":\"https://controls.example.test/bilateral-control/\",\"extra\":true}","{\"listenAddress\":\"127.0.0.1\",\"port\":44319,\"port\":44320,\"publicAppUrl\":\"https://controls.example.test/bilateral-control/\"}","{\"listenAddress\":\"127.0.0.1\",\"port\":44319}",new string(' ',4097)}){
                File.WriteAllText(selectionFile,json);var rejected=false;try{BrowserGatewayEndpoint.Load(selectionFile);}catch(Exception e)when(e is InvalidDataException or JsonException){rejected=true;}Check(rejected,"Ambiguous/unknown/missing/oversized gateway selection admitted");
            }
            Check(BrowserGatewayEndpoint.Load(null) is null,"Omitted gateway selection changed default");
            var relativeRejected=false;try{BrowserGatewayEndpoint.Load("relative.json");}catch(InvalidDataException){relativeRejected=true;}Check(relativeRejected,"Relative private selection accepted");
        }finally{File.Delete(selectionFile);}
        var duplicatePortRejected=false;try{new BrowserGatewayEndpoint(selection).Configure(WebApplication.CreateBuilder(),44319);}catch(InvalidDataException){duplicatePortRejected=true;}Check(duplicatePortRejected,"Gateway reused the numeric loopback port");
        var endpoint=new BrowserGatewayEndpoint(selection);
        DefaultHttpContext Context(string path="/api/hub/v2/status",string method="GET"){
            var c=new DefaultHttpContext();c.Request.Scheme="http";c.Request.Host=new HostString("192.168.20.10:44319");c.Request.Path=path;c.Request.Method=method;
            c.Connection.LocalIpAddress=IPAddress.Parse("192.168.20.10");c.Connection.LocalPort=44319;c.Response.Body=new MemoryStream();
            c.Request.Headers.Origin=Public;c.Request.Headers.Authorization="Bearer "+Token;return c;
        }
        async Task Assert(DefaultHttpContext c,bool admitted,string reason,int? status=null){
            var effects=0;await endpoint.Process(c,_=>{effects++;return Task.CompletedTask;},Local,Token);
            Check(effects==(admitted?1:0),reason);
            if(status is {} code)Check(c.Response.StatusCode==code,reason+" status");
            if(!admitted)Check(c.Response.Headers.CacheControl=="no-store"&&c.Response.Headers["Referrer-Policy"]=="no-referrer",reason+" cache/referrer");
        }
        await Assert(Context(),true,"Selected gateway bearer read denied");
        foreach(var path in new[]{"/api/hub/v2/status","/api/sources","/api/fixture","/fixture","/operator-stop","/observe/Q","/stream/Q","/"}){
            var c=Context(path);c.Request.Headers.Remove("Origin");await Assert(c,false,"Missing Origin reached gateway surface "+path);
        }
        foreach(var path in new[]{"/api/owner","/API/OWNER/captures","/api/%6fwner/capture-actions","/api%2fowner/captures","/%61pi/owner/captures","/api/%256fwner/captures"})
            foreach(var method in new[]{"GET","POST","OPTIONS"})await Assert(Context(path,method),false,"Private owner path admitted: "+path,403);
        foreach(var origin in new StringValues[]{StringValues.Empty,"null","https://foreign.test",Public+"/bilateral-control/",new([Public,Public]),Public+", "+Public}){
            var c=Context();c.Request.Headers.Origin=origin;await Assert(c,false,"Ambiguous or foreign Origin accepted");
        }
        foreach(var auth in new StringValues[]{StringValues.Empty,"Bearer wrong","Basic "+Token,"Bearer  "+Token,"Bearer "+Token+", Bearer "+Token,new(["Bearer "+Token,"Bearer "+Token])}){
            var c=Context();c.Request.Headers.Authorization=auth;await Assert(c,false,"Malformed or duplicate bearer accepted");
        }
        foreach(var query in new[]{"?token="+Token,"?token="+Token+"&token="+Token,"?token="}){var c=Context();c.Request.QueryString=new(query);await Assert(c,false,"REST query credentials accepted");}
        var wrongHost=Context();wrongHost.Request.Host=new HostString("foreign.test:44319");await Assert(wrongHost,false,"Wrong gateway Host accepted",403);
        var wrongPort=Context();wrongPort.Connection.LocalPort=44320;await Assert(wrongPort,false,"Unselected local port accepted",403);
        var wrongIp=Context();wrongIp.Connection.LocalIpAddress=IPAddress.Parse("192.168.20.11");await Assert(wrongIp,false,"Unselected local address accepted",403);
        var wrongScheme=Context();wrongScheme.Request.Scheme="https";await Assert(wrongScheme,false,"Forged HTTPS accepted",403);
        var forwarded=Context();forwarded.Request.Headers.Origin=Local;forwarded.Request.Headers["X-Forwarded-Host"]="127.0.0.1:43118";forwarded.Request.Headers["X-Forwarded-Proto"]="https";await Assert(forwarded,false,"Forwarded authority changed selected Origin");
        var local=Context("/api/owner/captures");local.Connection.LocalIpAddress=IPAddress.Loopback;local.Connection.LocalPort=43118;local.Request.Headers.Clear();local.Request.Host=new HostString("127.0.0.1:43118");await Assert(local,true,"Existing numeric loopback owner path changed");
        foreach(var path in new[]{"/observe/Q","/stream/W"}){
            var c=Context(path);c.Features.Set<IHttpWebSocketFeature>(new SocketFeature());c.Request.Headers.Remove("Authorization");c.Request.QueryString=new("?token="+Token);await Assert(c,true,"Authenticated selected gateway WS denied");
            c=Context(path);c.Features.Set<IHttpWebSocketFeature>(new SocketFeature());c.Request.QueryString=new("?token="+Token);await Assert(c,false,"WS accepted both credential sources");
            c=Context(path);c.Features.Set<IHttpWebSocketFeature>(new SocketFeature());c.Request.Headers.Remove("Authorization");c.Request.QueryString=new("?token="+Token+"&token="+Token);await Assert(c,false,"WS accepted duplicate query credentials");
            c=Context(path);c.Request.Headers.Remove("Authorization");c.Request.QueryString=new("?token="+Token);await Assert(c,false,"Plain HTTP received WS credential semantics");
        }
        var upgradedRest=Context();upgradedRest.Features.Set<IHttpWebSocketFeature>(new SocketFeature());upgradedRest.Request.Headers.Remove("Authorization");upgradedRest.Request.QueryString=new("?token="+Token);await Assert(upgradedRest,false,"REST Upgrade bypassed bearer requirement");
        DefaultHttpContext Preflight(string path="/api/hub/v2/actions"){
            var c=Context(path,"OPTIONS");c.Request.Headers.Remove("Authorization");c.Request.Headers["Access-Control-Request-Method"]="POST";c.Request.Headers["Access-Control-Request-Headers"]="Content-Type, aCcEpT, Authorization";return c;
        }
        var preflight=Preflight();await Assert(preflight,false,"Preflight dispatched effects",204);
        Check(preflight.Response.Headers["Access-Control-Allow-Origin"]==Public&&preflight.Response.Headers["Access-Control-Allow-Methods"]=="POST"&&preflight.Response.Headers["Access-Control-Allow-Headers"].ToString().Contains("Accept"),"Standard CORS with Blend Accept failed");
        Check(!preflight.Response.Headers.ContainsKey("Access-Control-Allow-Credentials")&&!preflight.Response.Headers.ContainsKey("Access-Control-Allow-Private-Network"),"Credentialed or deprecated PNA CORS emitted");
        foreach(var headers in new[]{"Authorization, authorization","Cookie","X-Other",new string('a',257)}){var c=Preflight();c.Request.Headers["Access-Control-Request-Headers"]=headers;await Assert(c,false,"Unsupported/ambiguous/oversized preflight headers accepted",403);}
        var multiple=Preflight();multiple.Request.Headers["Access-Control-Request-Method"]=new StringValues(["POST","POST"]);await Assert(multiple,false,"Multiple preflight methods accepted",403);
        var put=Preflight();put.Request.Headers["Access-Control-Request-Method"]="PUT";await Assert(put,false,"Unsupported preflight method accepted",403);
        await Assert(Preflight("/operator-stop/extra"),false,"Unsupported preflight route accepted",403);
        Console.WriteLine("PASS effect-free gateway Host/Origin/private-owner/all-surface boundaries, strict REST/WS credentials and bounded standard CORS preflight; numeric loopback preserved");
    }
    sealed class SocketFeature:IHttpWebSocketFeature{
        public bool IsWebSocketRequest=>true;
        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context)=>throw new InvalidOperationException("Unit guard must never upgrade a socket");
    }
    internal static async Task Http(int httpPort,int gatewayPort)
    {
        if(httpPort is <1024 or >65535||gatewayPort is <1024 or >65535||httpPort==gatewayPort)throw new InvalidDataException("Two distinct explicit isolated loopback ports required");
        var selection=new BrowserGatewaySelection("127.0.0.1",gatewayPort,"https://controls.example.test/bilateral-control/");selection.Validate();
        var endpoint=new BrowserGatewayEndpoint(selection);var local=$"http://127.0.0.1:{httpPort}";var remote=$"http://127.0.0.1:{gatewayPort}";
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=Path.GetFullPath("src/QuestMultiStream.Browser")});
        builder.Configuration.Sources.Clear();builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["Kestrel:Endpoints:Unselected:Url"]=local});
        endpoint.Configure(builder,httpPort);
        await using var app=builder.Build();app.UseWebSockets();endpoint.Guard(app,local,Token);app.UseDefaultFiles();app.UseStaticFiles();
        bool Auth(HttpContext c)=>endpoint.IsSelected(c)?endpoint.Authorized(c,Token):c.Request.Query["token"]==Token;
        using var lane=new SemaphoreSlim(1,1);OwnerV2Routes.Map(app,null,Auth,c=>endpoint.Origin(c,local),lane,()=>true);
        var effects=0;
        app.MapGet("/api/owner/fixture-private",()=>Results.Ok());
        app.MapGet("/api/sources",()=>Results.Ok(Array.Empty<object>()));
        app.MapGet("/api/captures",()=>Results.Ok(Array.Empty<object>()));
        app.MapPost("/operator-stop",()=>{Interlocked.Increment(ref effects);return Results.Accepted();});
        app.MapPost("/api/captures/{id}/{action}",()=>{Interlocked.Increment(ref effects);return Results.Accepted();});
        async Task SocketRoute(HttpContext c){
            if(!Auth(c)||!c.WebSockets.IsWebSocketRequest){c.Response.StatusCode=403;return;}
            using var socket=await c.WebSockets.AcceptWebSocketAsync();await socket.CloseAsync(WebSocketCloseStatus.NormalClosure,"effect-free conformance",c.RequestAborted);
        }
        app.MapGet("/observe/{id}",SocketRoute);app.MapGet("/stream/{id}",SocketRoute);
        using var client=new HttpClient(new SocketsHttpHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(10)};
        async Task CheckRequest(string path,HttpStatusCode expected,string? origin=Public,string? bearer=Token,string method="GET",string? host=null,string? requestMethod=null,string? requestHeaders=null,string? forwarded=null){
            using var request=new HttpRequestMessage(new HttpMethod(method),remote+path);
            if(origin is not null)request.Headers.TryAddWithoutValidation("Origin",origin);
            if(bearer is not null)request.Headers.TryAddWithoutValidation("Authorization","Bearer "+bearer);
            if(host is not null)request.Headers.Host=host;
            if(method=="POST")request.Content=new StringContent(JsonSerializer.Serialize(OwnerV2Tests.Action(),OwnerJson.Options),Encoding.UTF8,"application/json");
            if(requestMethod is not null)request.Headers.TryAddWithoutValidation("Access-Control-Request-Method",requestMethod);
            if(requestHeaders is not null)request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers",requestHeaders);
            if(forwarded is not null){request.Headers.TryAddWithoutValidation("X-Forwarded-Host",new Uri(local).Authority);request.Headers.TryAddWithoutValidation("X-Forwarded-Proto",forwarded);}
            using var response=await client.SendAsync(request);
            Check(response.StatusCode==expected,$"Actual gateway {method} {path.Split('?')[0]} returned {(int)response.StatusCode}, expected {(int)expected}");
            Check(response.Headers.CacheControl?.NoStore==true&&response.Headers.GetValues("Referrer-Policy").Single()=="no-referrer","Gateway response cache/referrer boundary lost");
            if(expected==HttpStatusCode.NoContent){
                Check(response.Headers.GetValues("Access-Control-Allow-Origin").Single()==Public,"Preflight origin differs");
                Check(!response.Headers.Contains("Access-Control-Allow-Credentials")&&!response.Headers.Contains("Access-Control-Allow-Private-Network"),"Unsafe CORS emitted");
                Check(response.Headers.GetValues("Access-Control-Allow-Headers").Single().Contains("Accept"),"Blend Accept preflight denied");
            }
        }
        async Task Socket(string path,string? origin=Public,string? query=Token,bool bearer=false,bool expected=false){
            using var ws=new ClientWebSocket();if(origin is not null)ws.Options.SetRequestHeader("Origin",origin);if(bearer)ws.Options.SetRequestHeader("Authorization","Bearer "+Token);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try{await ws.ConnectAsync(new Uri(remote.Replace("http:","ws:")+path+(query is null?"":"?token="+query)),timeout.Token);Check(expected,"Denied gateway WS accepted");await ws.CloseAsync(WebSocketCloseStatus.NormalClosure,"conformance complete",timeout.Token);}
            catch(WebSocketException)when(!expected){}
        }
        try{
            await app.StartAsync();
            Check(app.Urls.Order().SequenceEqual(new[]{local,remote}.Order()),"Inherited endpoints appended a third listener");
            await CheckRequest("/api/hub/v2/status",HttpStatusCode.ServiceUnavailable);
            await CheckRequest("/api/hub/v2/actions",HttpStatusCode.ServiceUnavailable,method:"POST");
            await CheckRequest("/api/hub/v2/status",HttpStatusCode.Forbidden,origin:null);
            await CheckRequest("/api/hub/v2/status",HttpStatusCode.Forbidden,origin:"null");
            await CheckRequest("/api/hub/v2/status",HttpStatusCode.Forbidden,origin:"https://foreign.test");
            await CheckRequest("/api/hub/v2/status",HttpStatusCode.Unauthorized,bearer:null);
            await CheckRequest("/api/hub/v2/status?token="+Token,HttpStatusCode.Unauthorized);
            await CheckRequest("/api/hub/v2/status?token="+Token,HttpStatusCode.Unauthorized,bearer:null);
            await CheckRequest("/api/hub/v2/status",HttpStatusCode.Unauthorized,bearer:Token+", Bearer "+Token);
            await CheckRequest("/api/hub/v2/status",HttpStatusCode.Forbidden,origin:Public+", "+Public);
            await CheckRequest("/api/hub/v2/status",HttpStatusCode.Forbidden,host:"other.example.test:"+gatewayPort);
            await CheckRequest("/api/hub/v2/status",HttpStatusCode.Forbidden,origin:local,forwarded:"https");
            foreach(var path in new[]{"/api/owner","/API/OWNER/fixture-private","/api/%6fwner/fixture-private","/api%2fowner/fixture-private","/api/%256fwner/fixture-private"}){
                await CheckRequest(path,HttpStatusCode.Forbidden);await CheckRequest(path,HttpStatusCode.Forbidden,bearer:null,method:"OPTIONS",requestMethod:"GET");
            }
            await CheckRequest("/api/hub/v2/actions",HttpStatusCode.NoContent,bearer:null,method:"OPTIONS",requestMethod:"POST",requestHeaders:"authorization, ACCEPT, content-type");
            await CheckRequest("/api/hub/v2/actions",HttpStatusCode.Forbidden,bearer:null,method:"OPTIONS",requestMethod:"POST",requestHeaders:"Cookie");
            await CheckRequest("/api/hub/v2/actions",HttpStatusCode.Forbidden,bearer:null,method:"OPTIONS",requestMethod:"POST",requestHeaders:"Accept, accept");
            await CheckRequest("/api/hub/v2/actions",HttpStatusCode.Forbidden,bearer:null,method:"OPTIONS",requestMethod:"PUT",requestHeaders:"Authorization");
            await CheckRequest("/operator-stop",HttpStatusCode.Forbidden,origin:null,method:"POST");
            await CheckRequest("/api/captures/Q/start",HttpStatusCode.Unauthorized,bearer:null,method:"POST");
            await CheckRequest("/api/sources",HttpStatusCode.Forbidden,origin:null);
            await CheckRequest("/api/captures",HttpStatusCode.Unauthorized,bearer:null);
            await CheckRequest("/",HttpStatusCode.Forbidden);
            Check(effects==0,"Denied request or preflight dispatched a mutation");
            foreach(var path in new[]{"/observe/Q","/stream/W"}){
                await Socket(path,expected:true);await Socket(path,origin:null);await Socket(path,origin:"https://foreign.test");await Socket(path,query:null);await Socket(path,bearer:true);await Socket(path,query:Token+"&token="+Token);
            }
            await Socket("/api/hub/v2/status");
            using var localRead=await client.GetAsync(local+"/api/hub/v2/status?token="+Token);Check(localRead.StatusCode==HttpStatusCode.ServiceUnavailable,"Existing numeric loopback query read changed");
            using var localPrivate=await client.GetAsync(local+"/api/owner/fixture-private");Check(localPrivate.StatusCode==HttpStatusCode.OK,"Private loopback owner route changed");
            await CheckRequest("/operator-stop",HttpStatusCode.Accepted,method:"POST");Check(effects==1,"Valid gateway command did not dispatch exactly once");
            Console.WriteLine("PASS real two-port loopback Kestrel gateway, production v2 routes, exact Host/Origin, bearer REST/query WS isolation, private-owner denial, effect-free CORS, no inherited listeners; no devices/capture/LAN effects");
        }finally{await app.StopAsync();}
    }
}
