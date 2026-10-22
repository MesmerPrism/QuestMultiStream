using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuestMultiStream.Browser;

// No listener/device. These checks invoke the production reader, retirement
// verifier and HTTP client with in-memory hostile owner responses.
internal static class OwnedCandidateTests
{
    public static async Task AuthTest(){await Client();await Replacement();await StopIntent();Console.WriteLine("PASS production live client MAC-only/key non-disclosure, replacement reply rejection, legacy Bearer preservation, fresh Stop intent during unresolved original; no listener");}
    public static async Task Run(string? fixturePython=null) {
        static byte[] StreamBytes(uint flags=1,ulong pts=10,bool truncated=false) {
            using var output=new MemoryStream();var head=new byte[32];"RMANVID1"u8.CopyTo(head);
            foreach(var(offset,value)in new[]{(8,1u),(12,1u),(16,1920u),(20,1080u),(28,2u)})BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(offset),value);
            output.Write(head);output.Write("{}"u8);
            void Packet(uint f,ulong stamp){var p=new byte[32];BinaryPrimitives.WriteUInt64BigEndian(p,stamp);BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(8),f);BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(12),8);output.Write(p);output.Write(new byte[]{0,0,0,1,0x67,0x64,0,0x28});}
            Packet(2,0);Packet(flags,pts);var bytes=output.ToArray();return truncated?bytes[..^1]:bytes;
        }
        var fanout=new CaptureFanout();using var first=fanout.Subscribe();using var second=fanout.Subscribe();
        var reader=new OwnedVideoReader(new MemoryStream(StreamBytes()));fanout.SetMetadata(await reader.Header(default));
        var config=await reader.Packet(default)??throw new Exception();var key=await reader.Packet(default)??throw new Exception();fanout.Publish(config);fanout.Publish(key);
        if(await reader.Packet(default)is not null)throw new Exception("Boundary EOF differs");
        using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(2))){await using var one=first.Read(deadline.Token).GetAsyncEnumerator();await using var two=second.Read(deadline.Token).GetAsyncEnumerator();foreach(var expected in new[]{config,key})if(!await one.MoveNextAsync()||!await two.MoveNextAsync()||!one.Current.SequenceEqual(expected)||!two.Current.SequenceEqual(expected))throw new Exception("Subscribe before config/key lost bootstrap");first.Dispose();using var late=fanout.Subscribe();if(late.WaitingKey)throw new Exception("Current GOP not bootstrapped");fanout.Publish(key);if(!await two.MoveNextAsync())throw new Exception("Viewer shutdown affected peer observer");}
        foreach(var data in new[]{StreamBytes(0),StreamBytes(4),StreamBytes(1,9007199254740992),StreamBytes(truncated:true)}){
            try{var bad=new OwnedVideoReader(new MemoryStream(data));await bad.Header(default);while(await bad.Packet(default)is not null){}throw new Exception("Hostile owned bytes accepted");}catch(Exception error)when(error is InvalidDataException or EndOfStreamException){}
        }
        var terminal=new OwnedTerminal("questms.display.terminal.v1","Q","invocation","generation","nonce","retired",0,true,true,true,true,"receipt");
        OwnedDisplayProducer.ValidateTerminal(terminal,"Q","invocation","generation","nonce",0,true,true);
        foreach(var invalid in new[]{terminal with{NativeChildExitCode=1},terminal with{StopNonce="foreign"},terminal with{ResourcesRetired=false},terminal with{NativeStdoutEof=false},terminal with{RemoteAckMatched=false}}){try{OwnedDisplayProducer.ValidateTerminal(invalid,"Q","invocation","generation","nonce",0,true,true);throw new Exception("False retirement accepted");}catch(InvalidDataException){}}
        try{OwnedDisplayProducer.ValidateTerminal(terminal,"Q","invocation","generation","nonce",1,true,true);throw new Exception("Local native1 marker accepted");}catch(InvalidDataException){}
        await AuthTest();if(fixturePython is not null)await Processes(fixturePython);Console.WriteLine("PASS owned candidate: production wire/config/key/EOF/unsafe/truncation, pre-config subscriptions/latejoin/observer independence, native1/nonce/resources/EOF retirement denials; authenticated live client controls; no listeners/devices");
    }
    static async Task Processes(string pythonPath){
        var root=Path.Combine(Path.GetTempPath(),"qms-owned-fixtures-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var script=Path.Combine(root,"producer.py");var binding=Path.Combine(root,"binding.json");File.WriteAllText(binding,"{}");
        File.WriteAllText(script,"""
import json,sys,struct,pathlib
assert sys.argv[1:] == ['display-stream-qms-v1']
raw=sys.stdin.buffer.readline();assert b'\r' not in raw
s=json.loads(raw)
assert s['schema']=='questms.display.start.v1'
pathlib.Path(s['outputDirectory']).mkdir(exist_ok=False)
meta=json.dumps({'invocation_id':s['invocationId'],'generation':s['generation']}).encode()
o=sys.stdout.buffer;o.write(struct.pack('>8s6I',b'RMANVID1',1,1,1920,1080,0,len(meta))+meta)
for flags,pts in [(2,0),(1,1),(0,2)]:
 p=bytes([0,0,0,1,103,100,0,40]);o.write(struct.pack('>QIIQQ',pts,flags,len(p),0,0)+p)
o.flush()
raw=sys.stdin.buffer.readline();assert b'\r' not in raw
c=json.loads(raw)
assert c['schema']=='questms.display.stop.v1'
for k in ['sourceId','invocationId','generation','stopNonce']:assert c[k]==s[k]
t={k:s[k] for k in ['sourceId','invocationId','generation','stopNonce']}
t.update(schema='questms.display.terminal.v1',state='retired',nativeChildExitCode=1 if s['sourceId']=='native1' else 0,nativeStdoutEof=True,nativeStderrEof=True,remoteAckMatched=True,resourcesRetired=True,receipt='modeled exact terminal')
with open(pathlib.Path(s['outputDirectory'])/'TERMINAL.json','x') as f:json.dump(t,f)
""");
        OwnedPin Pin(string path){var b=File.ReadAllBytes(path);return new(path,b.Length,Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant());}
        var q=new OwnedSource("Q","deviceQ","Fixture Q",Pin(binding));var w=q with{SourceId="W",DeviceId="deviceW"};var selected=new OwnedDisplaySelection("questms.display.selection.v1",Pin(pythonPath),Pin(script),root,[q,w]);
        using var qs=new CancellationTokenSource();using var ws=new CancellationTokenSource();var qp=new OwnedDisplayProducer(selected,q);var wp=new OwnedDisplayProducer(selected,w);var qr=qp.Run(new CaptureFanout(),qs.Token);var wr=wp.Run(new CaptureFanout(),ws.Token);
        var until=DateTimeOffset.UtcNow.AddSeconds(10);while((qp.Packets<3||wp.Packets<3)&&DateTimeOffset.UtcNow<until)await Task.Delay(20);
        if(qp.Packets!=3||wp.Packets!=3)throw new Exception("Owned fixture producer failed before stop");qs.Cancel();await qr.WaitAsync(TimeSpan.FromSeconds(10));if(!qp.RetirementProven||wr.IsCompleted)throw new Exception("One display stop retired the other display");ws.Cancel();await wr.WaitAsync(TimeSpan.FromSeconds(10));
        var badSource=q with{SourceId="native1"};var bad=new OwnedDisplayProducer(selected with{Sources=[badSource]},badSource);using var bs=new CancellationTokenSource();var br=bad.Run(new CaptureFanout(),bs.Token);until=DateTimeOffset.UtcNow.AddSeconds(10);while(bad.Packets<2&&DateTimeOffset.UtcNow<until)await Task.Delay(20);bs.Cancel();try{await br.WaitAsync(TimeSpan.FromSeconds(10));throw new Exception("Actual native1 terminal marker became retired");}catch(InvalidDataException){}if(bad.RetirementProven)throw new Exception("Unknown source released");
        await CaptureOwnerTests.RealProcesses(selected);
        Console.WriteLine("PASS production owned child process fixtures: two independent media pumps, exact stdin STOP, terminal/fullEOF join, peer survives single display shutdown, native1 marker denied; modeled producer only");
        Directory.Delete(root,true);
    }
    static async Task Client() {
        var path=Path.Combine(Path.GetTempPath(),"qms-client-"+Guid.NewGuid()+".json");var secret=new string('a',64);var epoch="fixture-owner";var id=Guid.NewGuid().ToString("D");
        File.WriteAllText(path,JsonSerializer.Serialize(new HubBridgeSelection("http://127.0.0.1:41234/operator",secret,"pair","session",null,epoch),HubControl.Json));
        try{
            using var transport=new FakeOwner(secret,epoch);using var client=new HubOwnerBridge(path,transport);
            await client.Current();await client.Devices();var action=new HubAction("establish","both","pair",id,true);var handler=new HubActionHandler(new HubControl(null),client);await handler.Handle(action with{Execute=false});if(transport.Posts!=0)throw new Exception("Review caused an effect");await handler.Handle(action);await handler.Handle(action);if(transport.Posts!=1)throw new Exception("Logical request replayed");await client.Status(id);
            try{await handler.Handle(action with{Action="disconnect"});throw new Exception("Conflicting original ID accepted");}catch(InvalidDataException){}
            var selection=new OwnerSelectionRequest("questms.owner.selection.v1",Guid.NewGuid().ToString("D"),epoch,1,[new("Q","deviceQ"),new("W","deviceW")]);await client.Select(selection);
            try{await client.Select(selection with{Members=[new("Q","deviceQ"),new("W","deviceQ")]});throw new Exception("Duplicate selected device accepted");}catch(InvalidDataException){}
            foreach(var hostile in new[]{"mac","nonce","epoch","missing","stale","extra","duplicate","native","future","missing-field","backward"}){transport.Hostile=hostile;try{await client.Current();throw new Exception("Hostile owner response accepted: "+hostile);}catch(Exception error)when(error is InvalidDataException or JsonException){} }
            transport.Hostile="effective-unconfirmed";try{await client.Status(id);throw new Exception("Effective without actual completion accepted");}catch(InvalidDataException){}
            transport.Hostile="";transport.Drop=true;var pending=action with{RequestId=Guid.NewGuid().ToString("D")};for(var i=0;i<2;i++){try{await client.Execute(pending);throw new Exception("Lost reply became success");}catch(HttpRequestException){}}
            if(transport.Posts!=2)throw new Exception("Unknown POST replayed");transport.Drop=false;await client.Status(pending.RequestId);if(transport.Posts!=2)throw new Exception("Reconcile GET effected");
            try{OwnerJson.Read<HubOwnerReadback>(Encoding.UTF8.GetBytes("{\"state\":\"queued\",\"State\":\"effective\"}"));throw new Exception("Case alias accepted");}catch(InvalidDataException){}
        }finally{File.Delete(path);}
    }
    static async Task StopIntent(){
        var path=Path.Combine(Path.GetTempPath(),"qms-stop-intent-"+Guid.NewGuid()+".json");var secret=new string('b',64);var epoch="fixture-owner";
        File.WriteAllText(path,JsonSerializer.Serialize(new HubBridgeSelection("http://127.0.0.1:41234/operator",secret,"pair","session",null,epoch),HubControl.Json));
        try{
            using var transport=new FakeOwner(secret,epoch);using var client=new HubOwnerBridge(path,transport);var handler=new HubActionHandler(new HubControl(null),client);
            foreach(var phase in new[]{"starting","running","unknown"}){
                transport.PendingPhase=phase;var status=await client.Current();if(status.Readiness.CanStop||!status.ReconciliationRequired||status.ActiveRequestId is null)throw new Exception("Fixture accidentally qualified cleanup");
                var stop=new HubAction("disconnect","both","pair",Guid.NewGuid().ToString("D"),true);var before=transport.Posts;
                await handler.Handle(stop with{Execute=false});if(transport.Posts!=before)throw new Exception("Stop review caused an effect");
                var result=(HubOwnerReadback)await handler.Handle(stop);await handler.Handle(stop);if(result.State!="queued"||result.ActualEffectsCompleted||transport.Posts!=before+1)throw new Exception("Unresolved Stop intent blocked or replayed");
            }
            transport.Hostile="stale";var count=transport.Posts;try{await handler.Handle(new("disconnect","both","pair",Guid.NewGuid().ToString("D"),true));throw new Exception("Stale owner allowed Stop dispatch");}catch(InvalidDataException){}if(transport.Posts!=count)throw new Exception("Failed fresh guard dispatched Stop");
        }finally{File.Delete(path);}
    }
    static async Task Replacement(){
        var path=Path.Combine(Path.GetTempPath(),"qms-replacement-"+Guid.NewGuid()+".json");var secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));var id=Guid.NewGuid().ToString("D");
        try{
            File.WriteAllText(path,JsonSerializer.Serialize(new HubBridgeSelection("http://127.0.0.1:41234/operator",secret,"pair","session",null,"fixture-owner"),HubControl.Json));
            using(var transport=new ReplacementOwner(secret,false))using(var client=new HubOwnerBridge(path,transport)){
                try{await client.Status(id);throw new Exception("Replacement server forged authenticated reply");}catch(InvalidDataException){}
                if(transport.Seen!=1||transport.SecretExposed)throw new Exception("Live request disclosed its reply authentication key");
            }
            File.WriteAllText(path,JsonSerializer.Serialize(new HubBridgeSelection("http://127.0.0.1:41234/operator",secret,"pair","session"),HubControl.Json));
            using(var transport=new ReplacementOwner(secret,true))using(var client=new HubOwnerBridge(path,transport)){var result=await client.Status(id);if(result.State!="queued"||transport.Seen!=1)throw new Exception("Legacy bridge contract changed");}
        }finally{File.Delete(path);}
    }
    // Models a different process occupying the port: it knows only its incoming
    // request. The configured secret is supplied separately solely to detect leaks.
    sealed class ReplacementOwner(string secret,bool legacy):HttpMessageHandler{
        public int Seen;public bool SecretExposed;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){
            Seen++;var body=request.Content is null?Array.Empty<byte>():await request.Content.ReadAsByteArrayAsync(cancellation);
            var observed=request.ToString()+Encoding.UTF8.GetString(body);SecretExposed=observed.Contains(secret,StringComparison.Ordinal);
            if(legacy){if(request.Headers.Authorization?.ToString()!="Bearer "+secret||request.Headers.Contains("X-Quest-Mac"))throw new Exception("Legacy request changed");}
            else if(request.Headers.Authorization is not null||SecretExposed)throw new Exception("Live request leaked authentication secret");
            var response=JsonSerializer.SerializeToUtf8Bytes(new HubOwnerReadback("queued",request.RequestUri!.Segments.Last(),"pair","session","replacement modeled receipt",false),OwnerJson.Options);
            var result=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(response)};
            if(!legacy){
                var epoch=request.Headers.GetValues("X-Quest-Owner-Epoch").Single();var nonce=request.Headers.GetValues("X-Quest-Nonce").Single();
                // The old Bearer+MAC implementation gave the replacement exactly
                // this signing key. Without it, public request MAC bytes cannot
                // sign the separately domain-separated reply.
                var observedKey=request.Headers.Authorization?.Parameter??request.Headers.GetValues("X-Quest-Mac").Single();
                result.Headers.Add("X-Quest-Owner-Epoch",epoch);result.Headers.Add("X-Quest-Nonce",nonce);result.Headers.Add("X-Quest-Mac",HubOwnerBridge.Mac(observedKey,"QMS-OWNER-REPLY-1","200",request.RequestUri.AbsolutePath,epoch,nonce,response));
            }
            return result;
        }
    }
    sealed class FakeOwner(string token,string epoch):HttpMessageHandler {
        public int Posts;public string Hostile="";public bool Drop;public string? PendingPhase;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation) {
            var body=request.Content is null?Array.Empty<byte>():await request.Content.ReadAsByteArrayAsync(cancellation);var path=request.RequestUri!.AbsolutePath;var nonce=request.Headers.GetValues("X-Quest-Nonce").Single();
            if(request.Headers.Authorization is not null||request.Headers.GetValues("X-Quest-Mac").Single()!=HubOwnerBridge.Mac(token,"QMS-OWNER-REQUEST-1",request.Method.Method,path,epoch,nonce,body))throw new Exception("Production request authentication differs");
            byte[] response;var now=DateTimeOffset.UtcNow;
            if(path.EndsWith("/status")||path.EndsWith("/selection")){
                var live=new OwnerLiveStatus("questms.owner.status.v1",epoch,Hostile=="backward"?0:1,Hostile=="stale"?now.AddSeconds(-12):Hostile=="future"?now.AddSeconds(1):now,now.AddSeconds(5),new("pair","session",[new("Q","deviceQ"),new("W","deviceW")]),"stopped",null,false,new(true,false,[]),new(null,null,[]),[],[],"receipt");
                if(PendingPhase is {} phase)live=live with{Phase=phase,ActiveRequestId="12345678-1234-1234-1234-123456789012",ReconciliationRequired=true,Readiness=new(false,false,["original-unknown"]),Native=phase=="running"?new("native-session","53",[new("Q","Q-process","50"),new("W","W-process","48")]):live.Native};
                var text=JsonSerializer.Serialize(live,OwnerJson.Options);if(Hostile=="extra")text=text[..^1]+",\"extra\":1}";if(Hostile=="duplicate")text=text.Replace("\"revision\":1","\"revision\":1,\"revision\":2");if(Hostile=="missing-field")text=text.Replace("\"reconciliationRequired\":false,","");if(Hostile=="native")text=text.Replace("\"actors\":[]","\"actors\":[{\"role\":\"Q\",\"processEpoch\":\"x\",\"nativeGeneration\":\"1\"}]");response=Encoding.UTF8.GetBytes(text);
            }else if(path.EndsWith("/devices"))response=JsonSerializer.SerializeToUtf8Bytes(new OwnerDevices("questms.owner.devices.v1",epoch,1,now,now.AddSeconds(5),[new("deviceQ","Headset Q",true,"ready"),new("deviceW","Headset W",true,"ready")]),OwnerJson.Options);
            else {var requestId=path.Split('/').Last();if(request.Method==HttpMethod.Post){Posts++;if(Drop)throw new HttpRequestException("Modeled post-effect lost reply");using var json=JsonDocument.Parse(body);requestId=json.RootElement.GetProperty("requestId").GetString()!;}response=JsonSerializer.SerializeToUtf8Bytes(new HubOwnerReadback(Hostile=="effective-unconfirmed"?"effective":"queued",requestId,"pair","session","receipt",false),OwnerJson.Options);}
            var result=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(response)};
            if(Hostile!="missing"){result.Headers.Add("X-Quest-Owner-Epoch",Hostile=="epoch"?"wrong":epoch);result.Headers.Add("X-Quest-Nonce",Hostile=="nonce"?"wrong":nonce);result.Headers.Add("X-Quest-Mac",Hostile=="mac"?new string('0',64):HubOwnerBridge.Mac(token,"QMS-OWNER-REPLY-1","200",path,epoch,nonce,response));}return result;
        }
    }
}
