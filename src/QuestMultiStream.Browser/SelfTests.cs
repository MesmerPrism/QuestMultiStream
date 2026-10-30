using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace QuestMultiStream.Browser;
internal static class SelfTests
{
    private static void SessionProjectionTests()
    {
        var root=Path.Combine(Path.GetTempPath(),"questms-member-session-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try {
            var source=Path.Combine(root,"fixture-publisher.txt");File.WriteAllText(source,"sanitized publisher fixture");
            var evidence=Path.Combine(root,"projection.json");var selected=Path.Combine(root,"selection.json");
            using var writer=Process.GetCurrentProcess();var key=RandomNumberGenerator.GetBytes(32);
            var actors=new Dictionary<string,SessionActor>{{"Q",new(100,200,10001,26,"app-process.26.fixture-q")},{"W",new(101,201,10002,25,"app-process.25.fixture-w")}};
            var native=new Dictionary<string,string>{{"Q","9223372036854775800"},{"W","9223372036854775799"}};
            var selection=new MemberSessionSelection("questms.hub.session-selection.v1",evidence,Convert.ToHexString(key),"session.fixture",writer.Id,writer.StartTime.ToUniversalTime().Ticks.ToString(),
                [new(source,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant())],actors,native);
            File.WriteAllText(selected,JsonSerializer.Serialize(selection,HubControl.Json));
            var old=DateTimeOffset.Parse("1970-01-01T00:00:01Z");
            var receipts=actors.ToDictionary(p=>p.Key,p=>new RetainedSessionReceipt(old,old.AddMinutes(1),p.Key+"_PEER_STATUS.receipt.json sha256:"+new string('a',64),"session.fixture",p.Value.AppProcessGeneration,native[p.Key],"active",false));
            var payload=new MemberSessionPayload("questms.hub.owner-projection.v2",new(){{"Q",26},{"W",25}},"session.fixture",DateTimeOffset.UtcNow,actors,[],native,false,"unsupported_without_authenticated_capture_monotonic_anchor",receipts);
            void Write(string body,bool badMac=false){var bytes=Encoding.UTF8.GetBytes(body);var mac=HMACSHA256.HashData(key,bytes);if(badMac)mac[0]^=1;File.WriteAllText(evidence,JsonSerializer.Serialize(new ProjectionEnvelope(body,Convert.ToHexString(mac)),HubControl.Json));}
            var original=JsonSerializer.Serialize(payload,HubControl.Json);Write(original);
            using var reader=new HubSessionProjection(selected);
            var value=reader.Read();
            if(value.CurrentAuthoritySupported || !value.PublisherAuthenticated || value.Members[0].AppProcessGeneration!=26 || value.Members[1].AppProcessGeneration!=25 || value.Members[0].NativeProcessGeneration!=native["Q"])
                throw new Exception("Independent application/native identities failed");
            // A native timestamp is retained evidence, not a comparison to the PC clock.
            var future=payload with {RetainedReceiptEvidence=receipts.ToDictionary(p=>p.Key,p=>p.Value with {ObservedUtc=old.AddYears(500),ExpiresUtc=old.AddYears(500).AddMinutes(1)})};
            Write(JsonSerializer.Serialize(future,HubControl.Json));if(reader.Read().CurrentAuthoritySupported)throw new Exception("Future device time became authority");
            void Denied(string body){Write(body);if(HubControl.SessionStatus(reader).State!="unavailable")throw new Exception("Invalid session accepted");}
            foreach(var mode in new[]{"schema","scalar","numeric-string","case-alias","native","actor","session","authority","observations","extra","missing","null","duplicate","stale","future","receipt"}) {
                var node=JsonNode.Parse(original)!.AsObject();
                switch(mode) {
                    case "schema":node["schema"]="questms.hub.owner-projection.v1";break;
                    case "scalar":node["generation"]=26;break;
                    case "numeric-string":node["generation"]!["Q"]="26";break;
                    case "case-alias":node["Session"]="session.fixture";break;
                    case "native":node["nativeProcessGenerations"]!["Q"]=26;break;
                    case "actor":node["actors"]!["Q"]!["process_epoch_id"]="app-process.25.wrong";break;
                    case "session":node["session"]="session.wrong";break;
                    case "authority":node["currentAuthoritySupported"]=true;break;
                    case "observations":node["observations"]!["Q.device"]=new JsonObject();break;
                    case "extra":node["unselected"]=true;break;
                    case "missing":node.Remove("currentAuthoritySupported");break;
                    case "null":node["actors"]!["Q"]=null;break;
                    case "duplicate":Denied(original.Replace("\"session\":\"session.fixture\"","\"session\":\"session.fixture\",\"session\":\"session.fixture\""));continue;
                    case "stale":node["createdUtc"]=DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");break;
                    case "future":node["createdUtc"]=DateTimeOffset.UtcNow.AddMinutes(1).ToString("O");break;
                    case "receipt":node["retainedReceiptEvidence"]!["W"]!["nativeProcessGeneration"]="1";break;
                }
                Denied(node.ToJsonString());
            }
            Write(original,true);if(HubControl.SessionStatus(reader).State!="unavailable")throw new Exception("Invalid MAC accepted");
            Write(original);File.WriteAllText(source,"changed");if(HubControl.SessionStatus(reader).State!="unavailable")throw new Exception("Changed writer source accepted");
            File.WriteAllText(source,"sanitized publisher fixture");
            // Numeric identities can coincide across different devices. No inequality policy.
            var coincidentActors=new Dictionary<string,SessionActor>{{"Q",actors["Q"]},{"W",actors["Q"] with {ProcessEpochId="app-process.26.fixture-w"}}};
            var coincidentNative=new Dictionary<string,string>{{"Q",native["Q"]},{"W",native["Q"]}};
            var coincidentSelection=selection with {Actors=coincidentActors,NativeProcessGenerations=coincidentNative};
            var coincidentPath=Path.Combine(root,"coincident.json");File.WriteAllText(coincidentPath,JsonSerializer.Serialize(coincidentSelection,HubControl.Json));
            var coincident=payload with {Actors=coincidentActors,Generation=new(){{"Q",26},{"W",26}},NativeProcessGenerations=coincidentNative,RetainedReceiptEvidence=[]};
            Write(JsonSerializer.Serialize(coincident,HubControl.Json));using(var equalReader=new HubSessionProjection(coincidentPath)){if(equalReader.Read().Members.Length!=2)throw new Exception("Legitimate coincident values rejected");}
            Write(original);
            var wrong=Path.Combine(root,"wrong-selection.json");File.WriteAllText(wrong,JsonSerializer.Serialize(selection with {WriterBirthUtcTicks="1"},HubControl.Json));
            var denied=false;try{using var invalid=new HubSessionProjection(wrong);}catch(InvalidDataException){denied=true;}if(!denied)throw new Exception("Wrong writer birth accepted");
            if(HubControl.SessionStatus(null).State!="unavailable")throw new Exception("Unselected publisher accepted");
            Console.WriteLine("Member session projection: mixed identities, real writer, MAC/source/shape/authority/freshness denials passed");
        } finally {Directory.Delete(root,true);}
    }
    public static async Task Run()
    {
        HubControl.SelfTest();
        HubProjectionSource.SelfTest();
        SessionProjectionTests();
        await HubOwnerBridge.SelfTest();
        await CaptureFanout.SelfTest();
        await RawReadProgressControls();
        var bytes = new byte[65+12+12+4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(65), 0x68323634);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(69), 1344);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(73), 704);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(77), (1UL<<62) | 12345);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(85), 4);
        bytes[^4]=0;bytes[^3]=0;bytes[^2]=1;bytes[^1]=0x65;
        var reader = new ScrcpyVideoReader(new FragmentStream(bytes));
        if(await reader.Header(default)!=(1344,704))throw new Exception("Actual header parser failed");
        var packet=await reader.Packet(default);
        if(packet is null || packet.Length!=16 || packet[^1]!=0x65 || await reader.Packet(default)!=null)throw new Exception("Packet/EOF parser failed");
        foreach(var length in new uint[]{0, uint.MaxValue})
        {
            var bad=(byte[])bytes.Clone();BinaryPrimitives.WriteUInt32BigEndian(bad.AsSpan(85),length);
            await Denied(bad, false);
        }
        await Denied(bytes[..^1],false);
        var wrong=(byte[])bytes.Clone();wrong[65]=0;await Denied(wrong,true);
        var dummy=(byte[])bytes.Clone();dummy[0]=1;await Denied(dummy,true);
        var source=new SourceSelection("fixture","Fixture","fixture","fixture","00000000-0000-0000-0000-000000000001","00000000-0000-0000-0000-000000000002",43119,2,"640:480:0:0");
        var args=HostSelection.ServerArguments(source,"12345678");
        if(!args.Contains("display_id=2")||!args.Contains("crop=640:480:0:0")||!args.Contains("control=true")||!args.Contains("audio=false")||!args.Contains("power_on=false")||!args.Contains("clipboard_autosync=false"))throw new Exception("Actual owner lifecycle-only profile adapter failed");
        var now=DateTimeOffset.UtcNow;
        foreach(var offset in new[]{-1,1})
        {
            using var doc=JsonDocument.Parse(JsonSerializer.Serialize(new { active_leases=new[]{new {id=source.LeaseId,resource="quest:"+source.Canonical,status="active",lease_until=now.AddSeconds(offset)}} }));
            if(HostSelection.LeaseMatches(doc.RootElement,source,now)!=(offset>0))throw new Exception("Actual expiry guard failed");
            if(HostSelection.LeaseMatches(doc.RootElement,source,now,now.AddSeconds(2)))throw new Exception("Capture outlives selected lease");
        }
        await StartupControls();
        await NoFrameCancellationControls();
        await PostDummyFailureControls();
        await EvidenceControls();
        await CloseControls();
        await LifetimeControls();
        Console.WriteLine("PASS actual fragmented packet/header/EOF, malformed length/codec/dummy, truncated body, owner display/crop profile controls; no device operations");
    }
    // Catches lost partial-read facts and fabricated complete packets on cancel/EOF.
    static async Task RawReadProgressControls()
    {
        static JsonElement Progress(ScrcpyVideoReader reader)
        {
            return JsonSerializer.SerializeToElement(reader.Progress.Snapshot());
        }
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var metadata = new byte[76];
        BinaryPrimitives.WriteUInt32BigEndian(metadata.AsSpan(64), 0x68323634);
        BinaryPrimitives.WriteInt32BigEndian(metadata.AsSpan(68), 1344);
        BinaryPrimitives.WriteInt32BigEndian(metadata.AsSpan(72), 704);
        byte[] Packet(ulong flags, params byte[] payload)
        {
            var packet = new byte[12 + payload.Length];
            BinaryPrimitives.WriteUInt64BigEndian(packet, flags);
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), (uint)payload.Length);
            payload.CopyTo(packet, 12); return packet;
        }
        var config = Packet(1UL << 63, 1, 2);
        var key = Packet((1UL << 62) | 12345, 3, 4, 5, 7);
        var delta = Packet(12346, 6);
        var largePts = Packet(4611686018427387903UL, 42);
        var reader = new ScrcpyVideoReader(new FragmentStream([..metadata, ..config, ..key, ..delta, ..largePts]));
        await reader.Header(default, dummyAlreadyRead:true);
        var p = Progress(reader);
        Check(p.GetProperty("MetadataComplete").GetBoolean() && p.GetProperty("WireBytes").GetInt64() == 76 && p.GetProperty("ConfigPackets").GetInt64() == 0 && p.GetProperty("FirstPacketUtc").ValueKind == JsonValueKind.Null, "Metadata became a complete packet or included the startup dummy");
        var before = DateTimeOffset.UtcNow;
        Check((await reader.Packet(default))!.SequenceEqual(config), "Progress changed config packet bytes");
        p = Progress(reader);
        Check(p.GetProperty("ConfigPackets").GetInt64() == 1 && p.GetProperty("LastPts").ValueKind == JsonValueKind.Null, "Config manufactured a media PTS");
        foreach (var expected in new[]{key,delta})
            Check((await reader.Packet(default))!.SequenceEqual(expected), "Progress changed original packet bytes");
        p = Progress(reader);
        Check(p.GetProperty("ConfigPackets").GetInt64() == 1 && p.GetProperty("KeyPackets").GetInt64() == 1 && p.GetProperty("DeltaPackets").GetInt64() == 1 && p.GetProperty("WireBytes").GetInt64() == 119, "Complete config/key/delta facts lost");
        Check(p.GetProperty("FirstPacketUtc").GetDateTimeOffset() >= before && p.GetProperty("LastPacketUtc").GetDateTimeOffset() <= DateTimeOffset.UtcNow, "Packet timestamps are not host receipt UTC");
        Check((await reader.Packet(default))!.SequenceEqual(largePts) && Progress(reader).GetProperty("LastPts").GetString() == "4611686018427387903", "Opaque PTS lost exact JSON precision above 2^53");
        Check(await reader.Packet(default) is null && Progress(reader).GetProperty("Outcome").GetString() == "eof", "Clean EOF lost");
        foreach (var count in new[]{0,3,15})
        {
            using var stream = new PrefixWaitStream([..metadata, ..key[..count]]);
            var partial = new ScrcpyVideoReader(stream);
            await partial.Header(default, dummyAlreadyRead:true);
            using var cancel = new CancellationTokenSource();
            var waiting = partial.Packet(cancel.Token);
            await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
            p = Progress(partial);
            Check(p.GetProperty("Stage").GetString() == (count < 12 ? "packet-header" : "packet-body") && p.GetProperty("StageBytes").GetInt32() == (count < 12 ? count : count - 12) && p.GetProperty("StageExpectedBytes").GetInt32() == (count < 12 ? 12 : 4) && p.GetProperty("WireBytes").GetInt64() == 76 + count && p.GetProperty("Outcome").GetString() == "reading", "Waiting partial packet position lost");
            cancel.Cancel();
            try { await waiting.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Cancellation accepted as packet"); }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) {}
            p = Progress(partial);
            Check(p.GetProperty("Outcome").GetString() == "cancelled" && p.GetProperty("KeyPackets").GetInt64() == 0 && p.GetProperty("WireBytes").GetInt64() == 76 + count, "Cancellation lost consumed bytes or manufactured complete packet");
        }
        foreach (var count in new[]{0,67,75})
        {
            using var stream = new PrefixWaitStream(metadata[..count]);
            var partial = new ScrcpyVideoReader(stream); using var cancel = new CancellationTokenSource();
            var waiting = partial.Header(cancel.Token, dummyAlreadyRead:true);
            await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
            p = Progress(partial);
            Check(p.GetProperty("Stage").GetString() == (count < 64 ? "device-name" : "metadata") && p.GetProperty("StageBytes").GetInt32() == (count < 64 ? count : count - 64) && p.GetProperty("WireBytes").GetInt64() == count && !p.GetProperty("MetadataComplete").GetBoolean(), "Partial metadata became ready or lost consumed bytes");
            cancel.Cancel();
            try { await waiting.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Partial metadata cancellation accepted"); }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) {}
            Check(Progress(partial).GetProperty("Outcome").GetString() == "cancelled", "Partial metadata cancellation lost");
        }
        var truncated = new ScrcpyVideoReader(new FragmentStream([..metadata, ..key[..14]]));
        await truncated.Header(default, dummyAlreadyRead:true);
        try { await truncated.Packet(default); throw new Exception("Truncated body accepted"); } catch (EndOfStreamException) {}
        p = Progress(truncated);
        Check(p.GetProperty("Outcome").GetString() == "failed" && p.GetProperty("Stage").GetString() == "packet-body" && p.GetProperty("StageBytes").GetInt32() == 2 && p.GetProperty("KeyPackets").GetInt64() == 0, "Truncation progress/complete-packet distinction lost");
        Console.WriteLine("PASS raw reader in-memory metadata, no packet bytes, partial header/body, cancellation, config/key/delta, opaque PTS, host UTC and EOF/truncation progress; no listener/device");
    }
    sealed class PrefixWaitStream(byte[] bytes):MemoryStream(bytes)
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
        {
            if (Position < Length) return await base.ReadAsync(buffer[..Math.Min(3,buffer.Length)],ct);
            Waiting.TrySetResult(); await Task.Delay(Timeout.Infinite,ct); return 0;
        }
    }
    static async Task LifetimeControls()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("de-DE");
            if (MonitorLifetime.ParseUtc("2026-10-12T01:32:12.3158427+00:00").Offset != TimeSpan.Zero) throw new Exception("UTC offset drift");
            foreach (var text in new[] {"10/11/2026 21:10:00", "12.10.2026 01:32:00", "2026-10-12T01:32:00+02:00", "2026-02-30T01:32:00Z"})
            { try { MonitorLifetime.ParseUtc(text); throw new Exception("Locale/invalid date admitted"); } catch (InvalidOperationException) {} }
            var options = new JsonSerializerOptions { Converters = {new StrictUtcDateConverter()} };
            try { JsonSerializer.Deserialize<DateTimeOffset>("\"10/11/2026 21:10:00\"", options); throw new Exception("Locale JSON deadline admitted"); } catch (InvalidOperationException) {}
            MonitorLifetime.ValidateCapture(MonitorLifetime.UntilStop, null, DateTimeOffset.UtcNow, true);
            foreach (var bad in new[] {("unknown",(DateTimeOffset?)null), (MonitorLifetime.Bounded,(DateTimeOffset?)null), (MonitorLifetime.UntilStop,(DateTimeOffset?)DateTimeOffset.UtcNow.AddHours(1))})
            { try { MonitorLifetime.ValidateCapture(bad.Item1,bad.Item2,DateTimeOffset.UtcNow,true); throw new Exception("Lifetime conflict admitted"); } catch (InvalidOperationException) {} }
            using var stop = new CancellationTokenSource(); using var capture = MonitorLifetime.CaptureCancellation(null,stop.Token);
            if(capture.IsCancellationRequested)throw new Exception("Until-stop spontaneously canceled");
            stop.Cancel();if(!capture.IsCancellationRequested)throw new Exception("Operator stop did not cancel actual capture token");
            if(MonitorLifetime.OperatorStopWithinScope(true,DateTimeOffset.UtcNow.AddSeconds(-1),DateTimeOffset.UtcNow))throw new Exception("Late stop relabeled actual expiry");
            var retired=false;var stopped=false;
            var waiter=MonitorLifetime.StopAfterRetirement(()=>retired,()=>stopped=true);
            await Task.Delay(100);if(stopped||waiter.IsCompleted)throw new Exception("Pending native ownership falsely released");
            retired=true;await waiter;if(!stopped)throw new Exception("Actual retirement did not complete stop");
        }
        finally {System.Globalization.CultureInfo.CurrentCulture=original;}
        Console.WriteLine("PASS production ISO UTC/JSON under de-DE, closed lifetime shapes, until-stop cancellation and held retirement predicate; no device calls");
    }
    static async Task CloseControls()
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        using var tcp=new TcpClient();var accept=listener.AcceptTcpClientAsync();
        await tcp.ConnectAsync(IPAddress.Loopback,((IPEndPoint)listener.LocalEndpoint).Port);
        using var peer=await accept;listener.Stop();
        using var client=WebSocket.CreateFromStream(tcp.GetStream(),false,null,Timeout.InfiniteTimeSpan);
        using var server=WebSocket.CreateFromStream(peer.GetStream(),true,null,Timeout.InfiniteTimeSpan);
        using var bound=new CancellationTokenSource(TimeSpan.FromSeconds(5));var observed=false;
        var monitor=BrowserConnection.ObserveClose(server,bound.Token,()=>observed=true);
        await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"viewer disconnected",bound.Token);
        await monitor;if(!observed)throw new Exception("Actual close frame did not cancel capture observation");
        Console.WriteLine("PASS real loopback WebSocket close observation; close alone does not certify native EOF/reaping");
    }
    static async Task EvidenceControls()
    {
        var dir=Path.Combine(Path.GetTempPath(),"questms-browser-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var body=new byte[2*1024*1024];Random.Shared.NextBytes(body);
        var evidence=new CaptureEvidence(dir,"normal");
        var result=await evidence.Drain(new MemoryStream(body),"stdout");
        var expected=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)).ToLowerInvariant();
        if(result.Bytes!=body.Length||result.Retained!=1024*1024||!result.Truncated||!result.Eof||result.CaptureError!=null||result.FullStreamSha256!=expected)throw new Exception("Actual bounded drain lost EOF/full hash truth");
        File.WriteAllBytes(Path.Combine(dir,"failed.stdout.raw"),new byte[]{42});
        var failed=new CaptureEvidence(dir,"failed");var denied=await failed.Drain(new MemoryStream(body),"stdout");
        if(denied.CaptureError==null||!denied.Eof||denied.Retained!=0||denied.FullStreamSha256!=expected||File.ReadAllBytes(Path.Combine(dir,"failed.stdout.raw"))[0]!=42)throw new Exception("Evidence failure stopped drain or overwrote original bytes");
        File.WriteAllText(Path.Combine(dir,"failed.001.stage.json"),"original");failed.SafeEvent("stage");
        if(failed.Errors.Count!=1||File.ReadAllText(Path.Combine(dir,"failed.001.stage.json"))!="original")throw new Exception("Stage failure was thrown/hidden or replaced original");
        File.WriteAllText(Path.Combine(dir,"intent.001.before-effect.json"),"original");
        try { new CaptureEvidence(dir,"intent").Event("before-effect"); throw new Exception("Failed intent allowed next effect"); } catch (IOException) {}
        Console.WriteLine("PASS actual bounded drain/full hash/EOF and CreateNew evidence failure preserving original bytes; scratch "+dir);
    }
    static async Task StartupControls()
    {
        var reservation=new TcpListener(IPAddress.Loopback,0);reservation.Start();var port=((IPEndPoint)reservation.LocalEndpoint).Port;reservation.Stop();
        var listener=new TcpListener(IPAddress.Loopback,port);var observations=new List<string>();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var refused=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection=ScrcpyStartup.Connect(port,()=>false,deadline.Token,s=>{observations.Add(s);if(s=="ConnectionRefused")refused.TrySetResult();});
        await refused.Task.WaitAsync(deadline.Token);listener.Start();
        using var peer=await listener.AcceptTcpClientAsync(deadline.Token);
        await peer.GetStream().WriteAsync(new byte[]{0},deadline.Token);
        using var client=await connection;listener.Stop();
        if(!observations.Contains("ConnectionRefused")||observations[^1]!="dummy-ready")throw new Exception("Same-launch readiness race control failed");
        try{await ScrcpyStartup.Connect(port,()=>true,deadline.Token,_=>{});throw new Exception("Exited child admitted");}catch(IOException){}
        using var expired=new CancellationTokenSource();expired.Cancel();
        try{await ScrcpyStartup.Connect(port,()=>false,expired.Token,_=>{});throw new Exception("Expired startup admitted");}catch(OperationCanceledException){}
        // Advance an observer-only estimated boundary without wall-clock waiting.
        // Actual until-stop scope still owns this delayed native readiness wait.
        var delayedListener=new TcpListener(IPAddress.Loopback,0);delayedListener.Start();
        using var actualStop=new CancellationTokenSource();using var actualScope=MonitorLifetime.CaptureCancellation(null,actualStop.Token);
        using var observerEstimate=new CancellationTokenSource();
        var delayed=ScrcpyStartup.Connect(((IPEndPoint)delayedListener.LocalEndpoint).Port,()=>false,actualScope.Token,_=>{});
        using var delayedPeer=await delayedListener.AcceptTcpClientAsync(deadline.Token);
        observerEstimate.Cancel();await Task.Yield();
        if(delayed.IsCompleted||actualScope.IsCancellationRequested)throw new Exception("Observer estimate retired a healthy waiting producer");
        await delayedPeer.GetStream().WriteAsync(new byte[]{0},actualScope.Token);using var delayedClient=await delayed;delayedListener.Stop();
        Console.WriteLine("PASS actual loopback delayed-listener readiness, exited-child and deadline denial; no server launch or device calls");
    }
    static async Task NoFrameCancellationControls()
    {
        using var scope=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var stop=new CancellationTokenSource();
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        Task<TcpClient>? lifecycleAccept=null;TcpClient? lifecyclePeer=null;
        try {
            var connected=ScrcpyStartup.Connect(((IPEndPoint)listener.LocalEndpoint).Port,()=>false,stop.Token,_=>{});
            using var videoPeer=await listener.AcceptTcpClientAsync(scope.Token);
            lifecycleAccept=listener.AcceptTcpClientAsync(scope.Token).AsTask();
            await videoPeer.GetStream().WriteAsync(new byte[]{0},scope.Token);
            using var client=await connected;
            try { lifecyclePeer=await lifecycleAccept.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch(TimeoutException){throw new Exception("No owned lifecycle connection can notify a no-first-frame producer of Stop");}
            var metadata=new byte[76];BinaryPrimitives.WriteUInt32BigEndian(metadata.AsSpan(64),0x68323634);
            BinaryPrimitives.WriteInt32BigEndian(metadata.AsSpan(68),1344);BinaryPrimitives.WriteInt32BigEndian(metadata.AsSpan(72),704);
            await videoPeer.GetStream().WriteAsync(metadata,scope.Token);
            var reader=new ScrcpyVideoReader(client.GetStream());await reader.Header(scope.Token,dummyAlreadyRead:true);
            var noPacket=reader.Packet(scope.Token);var input=lifecyclePeer.GetStream().ReadAsync(new byte[1],scope.Token).AsTask();
            if(noPacket.IsCompleted||input.IsCompleted)throw new Exception("Modeled no-frame producer did not retain both owners");
            stop.Cancel();
            if(await input.WaitAsync(scope.Token)!=0)throw new Exception("Lifecycle channel wrote input bytes instead of closing");
            if(noPacket.IsCompleted)throw new Exception("Stop notification fabricated producer media EOF");
            videoPeer.Dispose();if(await noPacket.WaitAsync(scope.Token)!=null)throw new Exception("Actual peer EOF did not retire the packet reader");
            Console.WriteLine("PASS actual loopback metadata/no-first-frame Stop: zero control bytes, lifecycle EOF, retained media owner until actual video EOF; no device");
        } finally {
            scope.Cancel();listener.Stop();lifecyclePeer?.Dispose();
            if(lifecycleAccept is not null)try{(await lifecycleAccept).Dispose();}catch(OperationCanceledException){}catch(SocketException){}
        }
    }
    static async Task PostDummyFailureControls()
    {
        using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        Task<ScrcpyConnection>? connecting=null;
        try {
            connecting=ScrcpyStartup.Connect(((IPEndPoint)listener.LocalEndpoint).Port,()=>false,stop.Token,_=>{});
            using var videoPeer=await listener.AcceptTcpClientAsync(stop.Token);
            listener.Stop(); // Existing video stays open, but the second connection must be refused.
            await videoPeer.GetStream().WriteAsync(new byte[]{0},stop.Token);
            try {using var unexpected=await connecting.WaitAsync(TimeSpan.FromSeconds(5));throw new Exception("Refused post-dummy lifecycle connection was accepted");}
            catch(SocketException e) when(e.SocketErrorCode==SocketError.ConnectionRefused){}
            catch(TimeoutException){throw new Exception("Post-dummy lifecycle failure reentered video readiness instead of propagating once");}
            Console.WriteLine("PASS actual loopback post-dummy lifecycle refusal propagates without video readiness replay; no device");
        } finally {
            stop.Cancel();listener.Stop();
            if(connecting is not null)try{(await connecting).Dispose();}catch(OperationCanceledException){}catch(SocketException){}
        }
    }
    static async Task Denied(byte[] data,bool header)
    {
        try {var r=new ScrcpyVideoReader(new FragmentStream(data));await r.Header(default);if(!header)await r.Packet(default);}
        catch(Exception e) when(e is InvalidDataException or EndOfStreamException){return;}
        throw new Exception("Malformed actual protocol accepted");
    }
    sealed class FragmentStream(byte[] bytes):MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
            => base.ReadAsync(buffer[..Math.Min(3,buffer.Length)],ct);
    }
}
