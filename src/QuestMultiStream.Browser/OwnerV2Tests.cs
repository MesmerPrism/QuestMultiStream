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
        await Numeric();
        await HttpRoutes();
        Console.WriteLine("PASS v2 closed parser, separate opaque peer identities, advisory publication age, authenticated exact paths, production HTTP auth/body/selection routes, retained POST/GET and lost-reply non-replay; no device effects");
    }
    static async Task Numeric(){
        var request=Action() with {Action="mode",RunChoice=null,Mode="blend",BlendAmountBits=1048576000};request.Validate(Epoch);
        if(JsonSerializer.Serialize(Action(),OwnerJson.Options).Contains("blendAmountBits"))throw new Exception("Omitted old request shape changed");
        var json=JsonSerializer.Serialize(request,OwnerJson.Options);
        foreach(var token in new[]{"null","true","1.0","1e0","2147483648"})Denied(()=>OwnerActionV2.Read(Encoding.UTF8.GetBytes(json.Replace("1048576000",token))).Validate(Epoch));
        Denied(()=>OwnerActionV2.Read(Encoding.UTF8.GetBytes("[]")));
        Denied(()=>new OwnerBlendAmountV2(1048576000,"0","42","retired_configuration","fixture").Validate());
        Denied(()=>(request with {Action="establish",RunChoice=new("new",null)}).Validate(Epoch));
        var path=Path.Combine(Directory.GetCurrentDirectory(),"out","numeric-fixture-"+Guid.NewGuid()+".json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try{
            File.WriteAllText(path,JsonSerializer.Serialize(new HubBridgeSelection("http://127.0.0.1:43123/operator",new string('k',43),"local","local",null,Epoch,2),HubControl.Json));
            var wire=new Transport();using var bridge=new HubOwnerBridge(path,wire);
            var denied=await bridge.ExecuteV2(request);
            if(denied.State!="failed"||denied.ActualEffectsCompleted||denied.Receipt!="blend_amount_unsupported_not_dispatched"||wire.Posts!=0)throw new Exception("Unsupported numeric request was not definitely declined before POST");
            var refusal=await bridge.StatusV2(request.RequestId,true);
            if(refusal!=denied||wire.NumericReads!=0)throw new Exception("Lost public refusal queried nonexistent private Original");
            try{await bridge.ExecuteV2(request with {BlendAmountBits=1056964608});throw new Exception("Changed numeric UUID accepted");}catch(InvalidDataException){}
            wire.BlendSupported=true;
            if((await bridge.CapabilitiesV2(true)).Features?.Single()!="blend_amount_bits_v1"||JsonSerializer.Serialize(await bridge.CapabilitiesV2(),OwnerJson.Options).Contains("features"))throw new Exception("Capabilities read representation changed old shape");
            if(JsonSerializer.Serialize(await bridge.CurrentV2(),OwnerJson.Options).Contains("blendAmount"))throw new Exception("Plain status extended old peer shape");
            var observed=await bridge.CurrentV2(true);if(observed.Peers.Any(p=>p.BlendAmount?.AmountBits!=1056964608))throw new Exception("Opted status lost retired observations");
            wire.LegacyNumericPost=true;var numeric=request with {RequestId="44444444-4444-4444-4444-444444444444"};
            try{await bridge.ExecuteV2(numeric);throw new Exception("Legacy effective POST certified numeric completion");}catch(HttpRequestException){}
            var effective=await bridge.StatusV2(numeric.RequestId,true);
            if(effective.Peers.Any(p=>p.BlendAmount?.AmountBits!=1048576000)||wire.Posts!=1)throw new Exception("Opted numeric GET lost exact Float32 bits or replayed setter");
            using var restartedBridge=new HubOwnerBridge(path,wire);
            var recovered=await restartedBridge.StatusV2(numeric.RequestId,true);
            if(recovered.Peers.Any(p=>p.BlendAmount?.AmountBits!=1048576000))throw new Exception("Private durable numeric readback lost after public cache restart");
            Console.WriteLine("PASS numeric v2 exact bits/old shapes, authenticated Accept profile, cached no-dispatch refusal, retained legacy-effective POST and qualified GET; no SDK effects");
        }finally{File.Delete(path);}
    }
    static void withInvalidMode(this OwnerActionV2 value)=>(value with {Mode="both"}).Validate(Epoch);
    sealed class Transport:HttpMessageHandler
    {
        public int Posts;public bool LoseReply;public bool TamperReply;public bool BlendSupported;public bool LegacyNumericPost;public int NumericReads;
        readonly Dictionary<string,OwnerActionV2> inputs=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Reply(request);
        async Task<HttpResponseMessage> Reply(HttpRequestMessage request){
            var body=request.Content is null?[]:await request.Content.ReadAsByteArrayAsync();
            var nonce=request.Headers.GetValues("X-Quest-Nonce").Single();var path=request.RequestUri!.AbsolutePath;
            var mac=HubOwnerBridge.Mac(new string('k',43),"QMS-OWNER-REQUEST-1",request.Method.Method,path,Epoch,nonce,body);
            if(request.Headers.GetValues("X-Quest-Mac").Single()!=mac||request.Headers.Authorization is not null)throw new Exception("Request authentication differs");
            var opted=request.Headers.Accept.ToString()==V2.BlendAccept;
            OwnerPeerV2 BlendPeer(string role,uint bits)=>Peer(role) with {Mode="blend",BlendAmount=new(bits,"7","42","retired_configuration","qualified fixture "+role)};
            object value;
            if(path.EndsWith("/capabilities"))value=new OwnerCapabilitiesV2("questms.owner.capabilities.v2",Epoch,2,["establish","normalStop","whole","mode"],["own","peer","blend"]){Features=opted&&BlendSupported?[V2.BlendFeature]:null};
            else if(path.EndsWith("/status"))value=BlendSupported?Current() with {Peers=[BlendPeer("Q",1056964608) with {BlendAmount=opted?BlendPeer("Q",1056964608).BlendAmount:null},BlendPeer("W",1056964608) with {BlendAmount=opted?BlendPeer("W",1056964608).BlendAmount:null}]}:Current();
            else if(path.EndsWith("/devices"))value=new OwnerDevicesV2("questms.owner.devices.v2",Epoch,1,DateTimeOffset.UtcNow,[new("deviceQ","Modeled headset Q",true,"fixture only"),new("deviceW","Modeled headset W",true,"fixture only")]);
            else if(path.EndsWith("/runs"))value=new OwnerRunsV2("questms.owner.runs.v2",Epoch,1,Selection,DateTimeOffset.UtcNow,[new(new string('c',32),"Recorded fixture run",true,"fixture only")]);
            else if(path.EndsWith("/selection"))value=Current();
            else{
                var input=request.Method==HttpMethod.Post?OwnerJson.Read<OwnerActionV2>(body):null;
                var effective=input is null||LegacyNumericPost&&input.BlendAmountBits is not null;
                if(input is not null){inputs[input.RequestId]=input;Posts++;if(LoseReply)throw new HttpRequestException("modeled reply lost after owner retained intent");}
                else{input=inputs.GetValueOrDefault(path.Split('/').Last());NumericReads++;}
                value=new OwnerResultV2("questms.owner.result.v2",input?.RequestId??path.Split('/').Last(),Epoch,Selection,input?.Action??"establish",effective?"effective":"queued",effective,
                    effective?[new(input?.BlendAmountBits is {} qb&&opted?BlendPeer("Q",qb):Peer("Q"),"effective"),new(input?.BlendAmountBits is {} wb&&opted?BlendPeer("W",wb):Peer("W"),"effective")]:[],"modeled result");
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
        var httpWire=new Transport();using var bridge=new HubOwnerBridge(config,httpWire);using var lane=new SemaphoreSlim(1,1);bool idle=true;
        OwnerV2Routes.Map(app,bridge,c=>c.Request.Query["token"]=="fixture",c=>"http://fixture-origin",lane,()=>idle);
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
            httpWire.BlendSupported=true;httpWire.LegacyNumericPost=true;
            var numeric=Action() with {RequestId="55555555-5555-5555-5555-555555555555",Action="mode",Mode="blend",RunChoice=null,BlendAmountBits=1048576000};
            var numericJson=JsonSerializer.Serialize(numeric,OwnerJson.Options);
            if((await Post("/api/hub/v2/actions",numericJson.Replace("1048576000","null"))).StatusCode!=HttpStatusCode.BadRequest)throw new Exception("Present-null amount passed public ingress");
            if((await Post("/api/hub/v2/actions",numericJson)).StatusCode!=HttpStatusCode.BadGateway)throw new Exception("Plain numeric effective POST was certified without qualified readback");
            using var read=new HttpRequestMessage(HttpMethod.Get,"/api/hub/v2/actions/"+numeric.RequestId+"?token=fixture");read.Headers.TryAddWithoutValidation("Accept",V2.BlendAccept);
            var numericReply=OwnerJson.Read<OwnerResultV2>(await (await client.SendAsync(read)).Content.ReadAsByteArrayAsync());
            if(numericReply.State!="effective"||numericReply.Peers.Any(p=>p.BlendAmount?.AmountBits!=1048576000)||httpWire.Posts!=2)throw new Exception("Production numeric route/profile/Original readback differs");
            idle=false;var selection=JsonSerializer.Serialize(new OwnerSelectionV2("questms.owner.selection.v2",Request,Epoch,1,[new("Q","deviceQ"),new("W","deviceW")]),OwnerJson.Options);
            if((await Post("/api/hub/v2/selection",selection)).StatusCode!=HttpStatusCode.Conflict)throw new Exception("Selection crossed held display scope");
            idle=true;if((await Post("/api/hub/v2/selection",selection)).StatusCode!=HttpStatusCode.OK)throw new Exception("Production selection route failed");
        }finally{await app.StopAsync();File.Delete(config);}
    }
    internal static async Task ServeFixture(){
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=Directory.GetCurrentDirectory()});
        builder.Configuration.Sources.Clear();builder.Logging.ClearProviders();
        // An explicit fixture port permits a machine coordination lease before binding.
        var fixturePort=int.TryParse(Environment.GetEnvironmentVariable("QMS_FIXTURE_PORT"),out var selectedPort)?selectedPort:0;
        if(fixturePort!=0&&fixturePort is <1024 or >65535)throw new InvalidDataException("Invalid fixture port");
        builder.WebHost.ConfigureKestrel(o=>o.Listen(IPAddress.Loopback,fixturePort));
        await using var app=builder.Build();
        var config=Path.Combine(Path.GetTempPath(),"questms-render-"+Guid.NewGuid()+".json");
        File.WriteAllText(config,JsonSerializer.Serialize(new HubBridgeSelection("http://127.0.0.1:43123/operator",new string('k',43),"local","local",null,Epoch,2),HubControl.Json));
        using var bridge=new HubOwnerBridge(config,new Transport{BlendSupported=true,LegacyNumericPost=true});using var lane=new SemaphoreSlim(1,1);
        try{
            app.UseDefaultFiles();app.UseStaticFiles();app.MapPost("/fixture-stop",()=>{app.Lifetime.StopApplication();return Results.Ok();});
            OwnerV2Routes.Map(app,bridge,c=>c.Request.Query["token"]=="fixture",c=>app.Urls.Single(),lane,()=>true);
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
