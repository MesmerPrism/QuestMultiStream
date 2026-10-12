using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;

namespace QuestMultiStream.Browser;
internal static class SelfTests
{
    public static async Task Run()
    {
        HubControl.SelfTest();
        HubProjectionSource.SelfTest();
        await HubOwnerBridge.SelfTest();
        await CaptureFanout.SelfTest();
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
        if(!args.Contains("display_id=2")||!args.Contains("crop=640:480:0:0")||!args.Contains("control=false")||!args.Contains("audio=false"))throw new Exception("Actual owner profile adapter failed");
        var now=DateTimeOffset.UtcNow;
        foreach(var offset in new[]{-1,1})
        {
            using var doc=JsonDocument.Parse(JsonSerializer.Serialize(new { active_leases=new[]{new {id=source.LeaseId,resource="quest:"+source.Canonical,status="active",lease_until=now.AddSeconds(offset)}} }));
            if(HostSelection.LeaseMatches(doc.RootElement,source,now)!=(offset>0))throw new Exception("Actual expiry guard failed");
            if(HostSelection.LeaseMatches(doc.RootElement,source,now,now.AddSeconds(2)))throw new Exception("Capture outlives selected lease");
        }
        await StartupControls();
        await EvidenceControls();
        await CloseControls();
        await LifetimeControls();
        Console.WriteLine("PASS actual fragmented packet/header/EOF, malformed length/codec/dummy, truncated body, owner display/crop profile controls; no device operations");
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
