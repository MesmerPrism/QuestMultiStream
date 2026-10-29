using System.Text.Json;
using System.Text;
using System.Net;

namespace QuestMultiStream.Browser;

internal static class OwnerV2Tests
{
    internal const string Epoch="fixture-owner";
    internal const string Selection="11111111-1111-1111-1111-111111111111";
    internal const string Request="22222222-2222-2222-2222-222222222222";
    internal static OwnerPeerV2 Peer(string role)=>new(role,"device"+role,"50","app-process.50."+role,role=="Q"?"17":"19",
        role=="Q"?"854449628012777927":"854449628012777928",new string(role=="Q"?'a':'b',64),
        "session.duplex."+role,new string('c',32),"own",new("retained","100","200"),"modeled receipt "+role);
    internal static OwnerStatusV2 Current()=>new("questms.owner.status.v2",Epoch,1,DateTimeOffset.UtcNow,Selection,
        [new("Q","deviceQ"),new("W","deviceW")],"running",null,false,new(false,true,false,true,[]),
        [Peer("Q"),Peer("W")],"modeled status");
    internal static OwnerActionV2 Action()=>new("questms.owner.action.v2",Request,Epoch,Selection,1,"establish",new("new",null),"own");
    static void Denied(Action test){try{test();throw new Exception("Malformed v2 accepted");}catch(InvalidDataException){}catch(JsonException){}}
    internal static async Task Run()
    {
        Current().Validate(Epoch);Action().Validate(Epoch);
        if(Current().Peers[0].NativeGeneration==Current().Peers[1].NativeGeneration)throw new Exception("Independent fixture identities lost");
        (Current() with {Peers=[Peer("Q"),Peer("Q") with {Role="W",DeviceId="deviceW"}]}).Validate(Epoch);
        Denied(()=>Action().withInvalidMode());
        Denied(()=> (Action() with {RunChoice=null}).Validate(Epoch));
        Denied(()=> (Action() with {RunChoice=new("resume",null)}).Validate(Epoch));
        Denied(()=> (Action() with {Action="normalStop"}).Validate(Epoch));
        Denied(()=> (Current() with {Peers=[Peer("Q"),Peer("Q")]}).Validate(Epoch));
        Denied(()=> (Current() with {Peers=[Peer("Q") with {NativeGeneration="01"},Peer("W")]}).Validate(Epoch));
        Denied(()=> (Current() with {ReconciliationRequired=true}).Validate(Epoch));
        var json=JsonSerializer.Serialize(Current(),OwnerJson.Options);
        foreach(var invalid in new[]{json.Replace("\"revision\":1","\"revision\":1,\"revision\":1"),
            json.Replace("\"nativeProcessGeneration\":\"854449628012777927\"","\"nativeProcessGeneration\":854449628012777927"),
            json.Replace("\"nativeProcessGeneration\":","\"hasActualIdentity\":true,\"nativeProcessGeneration\":"),
            json.Replace("\"schema\":","\"unselected\":true,\"schema\":")})
            Denied(()=>OwnerJson.Read<OwnerStatusV2>(Encoding.UTF8.GetBytes(invalid)).Validate(Epoch));
        var stale=Current() with {ObservedUtc=DateTimeOffset.UtcNow.AddDays(-1)};
        stale.Validate(Epoch); // Publication age advisory; cannot refresh device-native facts.
        if(stale.Peers[0].NativeFreshness.State!="retained")throw new Exception("Publication refreshed native facts");
        var path=Path.Combine(Path.GetTempPath(),"questms-v2-"+Guid.NewGuid()+".json");
        var wire=new Transport();
        try{
            File.WriteAllText(path,JsonSerializer.Serialize(new HubBridgeSelection("http://127.0.0.1:43123/operator",new string('k',43),"local-selection","local-session",null,Epoch,2),HubControl.Json));
            using var bridge=new HubOwnerBridge(path,wire);
            await bridge.CapabilitiesV2();
            var current=await bridge.CurrentV2();if(current.Peers[0].NativeSession==current.Peers[1].NativeSession)throw new Exception("Fabricated common session");
            wire.TamperReply=true;
            try{await bridge.CurrentV2();throw new Exception("Unauthenticated v2 reply accepted");}catch(InvalidDataException){}
            wire.TamperReply=false;
            var first=await bridge.ExecuteV2(Action());await bridge.ExecuteV2(Action());
            if(first.State!="queued"||wire.Posts!=1)throw new Exception("Retained POST failed");
            try{await bridge.ExecuteV2(Action() with {Mode="peer"});throw new Exception("Conflicting replay accepted");}catch(InvalidDataException){}
            var result=await bridge.StatusV2(Request);
            if(result.State!="effective"||wire.Posts!=1)throw new Exception("GET replayed mutation");
            wire.LoseReply=true;var lost=Action() with {RequestId="33333333-3333-3333-3333-333333333333"};
            try{await bridge.ExecuteV2(lost);throw new Exception("Lost reply accepted");}catch(HttpRequestException){}
            try{await bridge.ExecuteV2(lost);}catch(HttpRequestException){}
            if(wire.Posts!=2)throw new Exception("Lost POST replayed");
            await bridge.StatusV2(lost.RequestId);if(wire.Posts!=2)throw new Exception("Reconciliation mutated");
        }finally{File.Delete(path);}
        await HttpRoutes();
        Console.WriteLine("PASS v2 closed parser, separate opaque peer identities, advisory publication age, authenticated exact paths, production HTTP auth/body/selection routes, retained POST/GET and lost-reply non-replay; no device effects");
    }
    static void withInvalidMode(this OwnerActionV2 value)=>(value with {Mode="both"}).Validate(Epoch);
    sealed class Transport:HttpMessageHandler
    {
        public int Posts;public bool LoseReply;public bool TamperReply;
        readonly Dictionary<string,OwnerActionV2> inputs=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Reply(request);
        async Task<HttpResponseMessage> Reply(HttpRequestMessage request){
            var body=request.Content is null?[]:await request.Content.ReadAsByteArrayAsync();
            var nonce=request.Headers.GetValues("X-Quest-Nonce").Single();var path=request.RequestUri!.AbsolutePath;
            var mac=HubOwnerBridge.Mac(new string('k',43),"QMS-OWNER-REQUEST-1",request.Method.Method,path,Epoch,nonce,body);
            if(request.Headers.GetValues("X-Quest-Mac").Single()!=mac||request.Headers.Authorization is not null)throw new Exception("Request authentication differs");
            object value;
            if(path.EndsWith("/capabilities"))value=new OwnerCapabilitiesV2("questms.owner.capabilities.v2",Epoch,2,["establish","normalStop","whole","mode"],["own","peer","blend"]);
            else if(path.EndsWith("/status"))value=Current();
            else if(path.EndsWith("/devices"))value=new OwnerDevicesV2("questms.owner.devices.v2",Epoch,1,DateTimeOffset.UtcNow,[new("deviceQ","Modeled headset Q",true,"fixture only"),new("deviceW","Modeled headset W",true,"fixture only")]);
            else if(path.EndsWith("/runs"))value=new OwnerRunsV2("questms.owner.runs.v2",Epoch,1,Selection,DateTimeOffset.UtcNow,[new(new string('c',32),"Recorded fixture run",true,"fixture only")]);
            else if(path.EndsWith("/selection"))value=Current();
            else{
                var input=request.Method==HttpMethod.Post?OwnerJson.Read<OwnerActionV2>(body):null;
                var effective=input is null;
                if(input is not null){inputs[input.RequestId]=input;Posts++;if(LoseReply)throw new HttpRequestException("modeled reply lost after owner retained intent");}
                else input=inputs.GetValueOrDefault(path.Split('/').Last());
                value=new OwnerResultV2("questms.owner.result.v2",input?.RequestId??path.Split('/').Last(),Epoch,Selection,input?.Action??"establish",effective?"effective":"queued",effective,
                    effective?[new(Peer("Q"),"effective"),new(Peer("W"),"effective")]:[],"modeled result");
            }
            var bytes=JsonSerializer.SerializeToUtf8Bytes(value,OwnerJson.Options);
            var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
            response.Headers.Add("X-Quest-Owner-Epoch",Epoch);response.Headers.Add("X-Quest-Nonce",nonce);
            response.Headers.Add("X-Quest-Mac",TamperReply?new string('0',64):HubOwnerBridge.Mac(new string('k',43),"QMS-OWNER-REPLY-1","200",path,Epoch,nonce,bytes));return response;
        }
    }
    static async Task HttpRoutes(){
        var builder=WebApplication.CreateSlimBuilder();builder.Configuration.Sources.Clear();builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o=>o.Listen(IPAddress.Loopback,0));await using var app=builder.Build();
        var config=Path.Combine(Path.GetTempPath(),"questms-route-"+Guid.NewGuid()+".json");
        File.WriteAllText(config,JsonSerializer.Serialize(new HubBridgeSelection("http://127.0.0.1:43123/operator",new string('k',43),"local","local",null,Epoch,2),HubControl.Json));
        using var bridge=new HubOwnerBridge(config,new Transport());using var lane=new SemaphoreSlim(1,1);bool idle=true;
        OwnerV2Routes.Map(app,bridge,c=>c.Request.Query["token"]=="fixture",()=>"http://fixture-origin",lane,()=>idle);
        try{
            await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri(app.Urls.Single())};
            if((await client.GetAsync("/api/hub/v2/status")).StatusCode!=HttpStatusCode.Unauthorized)throw new Exception("Unauthorized route accepted");
            var current=await client.GetByteArrayAsync("/api/hub/v2/status?token=fixture");OwnerJson.Read<OwnerStatusV2>(current).Validate(Epoch);
            async Task<HttpResponseMessage> Post(string path,string json,bool origin=true){using var message=new HttpRequestMessage(HttpMethod.Post,path+"?token=fixture"){Content=new StringContent(json,Encoding.UTF8,"application/json")};if(origin)message.Headers.Add("Origin","http://fixture-origin");return await client.SendAsync(message);}
            var action=JsonSerializer.Serialize(Action(),OwnerJson.Options);
            if((await Post("/api/hub/v2/actions",action,false)).StatusCode!=HttpStatusCode.Unauthorized)throw new Exception("Cross-origin mutation accepted");
            if((await Post("/api/hub/v2/actions",action.Replace("\"schema\":","\"extra\":true,\"schema\":"))).StatusCode!=HttpStatusCode.BadRequest)throw new Exception("Unmapped action accepted");
            if((await Post("/api/hub/v2/actions",action)).StatusCode!=HttpStatusCode.OK)throw new Exception("Production action route failed");
            var readback=OwnerJson.Read<OwnerResultV2>(await client.GetByteArrayAsync("/api/hub/v2/actions/"+Request+"?token=fixture"));readback.Validate(Epoch,Request);
            idle=false;var selection=JsonSerializer.Serialize(new OwnerSelectionV2("questms.owner.selection.v2",Request,Epoch,1,[new("Q","deviceQ"),new("W","deviceW")]),OwnerJson.Options);
            if((await Post("/api/hub/v2/selection",selection)).StatusCode!=HttpStatusCode.Conflict)throw new Exception("Selection crossed held display scope");
            idle=true;if((await Post("/api/hub/v2/selection",selection)).StatusCode!=HttpStatusCode.OK)throw new Exception("Production selection route failed");
        }finally{await app.StopAsync();File.Delete(config);}
    }
    internal static async Task ServeFixture(){
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=Directory.GetCurrentDirectory()});
        builder.Configuration.Sources.Clear();builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(o=>o.Listen(IPAddress.Loopback,0));
        await using var app=builder.Build();
        var config=Path.Combine(Path.GetTempPath(),"questms-render-"+Guid.NewGuid()+".json");
        File.WriteAllText(config,JsonSerializer.Serialize(new HubBridgeSelection("http://127.0.0.1:43123/operator",new string('k',43),"local","local",null,Epoch,2),HubControl.Json));
        using var bridge=new HubOwnerBridge(config,new Transport());using var lane=new SemaphoreSlim(1,1);
        try{
            app.UseDefaultFiles();app.UseStaticFiles();app.MapPost("/fixture-stop",()=>{app.Lifetime.StopApplication();return Results.Ok();});
            OwnerV2Routes.Map(app,bridge,c=>c.Request.Query["token"]=="fixture",()=>app.Urls.Single(),lane,()=>true);
            app.MapGet("/api/hub/capabilities",()=>Results.Json(new{protocolVersion=2,bridgeSelected=true,liveSupported=true,actions=V2.Actions}));
            app.MapGet("/api/sources",()=>Results.Json(Array.Empty<object>()));app.MapGet("/api/captures",()=>Results.Json(Array.Empty<object>()));
            await app.StartAsync();Console.WriteLine(app.Urls.Single()+"/#token=fixture");
            await app.WaitForShutdownAsync();
        }finally{await app.StopAsync();File.Delete(config);}
    }
    internal static async Task ReadWire(string selectionPath,string? requestId){
        using var bridge=new HubOwnerBridge(selectionPath);
        await bridge.CapabilitiesV2();var devices=await bridge.DevicesV2();var current=await bridge.CurrentV2();var runs=await bridge.RunsV2();
        V2.Require(current.SelectionId==runs.SelectionId,"Run choices and selected pair differ");
        if(requestId is not null){var result=await bridge.StatusV2(requestId);V2.Require(result.SelectionId==current.SelectionId,"Result belongs to another selected pair");}
        Console.WriteLine("PASS authenticated private-owner v2 wire: capabilities/devices/status/runs"+(requestId is null?"":"/original-result")+"; GET only, no action dispatch");
    }
}
