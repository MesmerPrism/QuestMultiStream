using System.Text.Json;
namespace QuestMultiStream.Browser;

internal static class NativeSquareCaptureTests
{
    static void FinalEvidence()
    {
        var root=Path.Combine(Path.GetTempPath(),"qms-native-terminal-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var normal=new CaptureEvidence(root,"normal");normal.Event("client-retired",new { mode="MODELED" });
            var complete=NativeSquareCapture.RetainTerminalEvidence(normal,true,null);
            if(!complete.Retired||complete.Failure is not null||normal.Errors.Count!=0)
                throw new Exception("Retained native terminal was held");
            var denied=new CaptureEvidence(root,"denied");denied.Event("client-retired",new { mode="MODELED" });
            var path=Path.Combine(root,"denied.002.terminal.json");File.WriteAllText(path,"original terminal collision");
            if(denied.Errors.Count!=0)throw new Exception("Fixture failed before terminal append");
            var held=NativeSquareCapture.RetainTerminalEvidence(denied,true,null);
            if(held.Retired||held.Failure is not IOException||denied.Errors.Count!=1||
                File.ReadAllText(path)!="original terminal collision"||!File.Exists(Path.Combine(root,"denied.001.client-retired.json")))
                throw new Exception("Final-event-only retention failure released native original or changed retained bytes");
            Console.WriteLine("PASS real CaptureEvidence final-event-only CreateNew failure holds native original; prior evidence and colliding original preserved");
        }
        finally { Directory.Delete(root,true); }
    }
    public static async Task Run(string? fixture,string? base64Fixture=null)
    {
        NativeCaptureBindingTests.Run();
        FinalEvidence();
        string invocation=new('a',32),generation=new('b',32),nonce=new('c',32);
        var row=JsonSerializer.Serialize(new{ @event="TERMINAL",invocation,generation,stop_nonce=nonce,
            state="retired",stop_nonce_matched=true,own_resources_retired=true,native_stop_returned=true,
            native_server_acknowledged=false});
        if(NativeSquareCapture.TerminalRecord("diagnostic\n"+row,invocation,generation,nonce) is null)
            throw new Exception("Exact native terminal missing");
        foreach(var changed in new[]{(new string('d',32),generation,nonce),(invocation,new string('d',32),nonce),(invocation,generation,new string('d',32))})
        {
            try{NativeSquareCapture.TerminalRecord(row,changed.Item1,changed.Item2,changed.Item3);throw new Exception("Foreign terminal accepted");}
            catch(InvalidDataException){}
        }
        try{NativeSquareCapture.TerminalRecord(row+"\n"+row,invocation,generation,nonce);throw new Exception("Duplicate terminal accepted");}
        catch(InvalidDataException){}
        if(NativeSquareCapture.TerminalRecord("{\"event\":\"READY\"}",invocation,generation,nonce) is not null)
            throw new Exception("READY was mistaken for retirement");
        if(!NativeCaptureSelection.Mode("raw-two-lens")||!NativeCaptureSelection.Mode("square-left")||NativeCaptureSelection.Mode("left")||NativeCaptureSelection.Mode(""))
            throw new Exception("Capture mode validation differs");
        Console.WriteLine("PASS native terminal exact identities, duplicate/missing rejection, closed source modes");
        if(fixture is not null)
        {
            await using var stream=File.OpenRead(fixture);
            var reader=new OwnedVideoReader(stream);
            using var metadata=JsonDocument.Parse(JsonSerializer.Serialize(await reader.Header(CancellationToken.None)));
            if(metadata.RootElement.GetProperty("Width").GetUInt32()!=1280||metadata.RootElement.GetProperty("Height").GetUInt32()!=1280)
                throw new Exception("Java wire dimensions differ");
            int packets=0;while(await reader.Packet(CancellationToken.None) is not null)packets++;
            if(packets<2)throw new Exception("Java config/keyframe wire missing");
            Console.WriteLine("PASS actual Java-generated binary fixture through production owned video reader");
            if(base64Fixture is not null)
            {
                var encoded=await File.ReadAllTextAsync(base64Fixture);
                // Exercise the real production decoding boundary after the
                // observed Windows adb LF expansion, including whitespace.
                encoded=encoded.Replace("\r\n","\n").Replace("\n","\r\n");
                using var input=new MemoryStream(System.Text.Encoding.ASCII.GetBytes(encoded));
                using var decoded=NativeSquareCapture.DecodeMedia(input);
                using var actual=new MemoryStream();await decoded.CopyToAsync(actual);
                var expected=await File.ReadAllBytesAsync(fixture);
                if(!actual.ToArray().AsSpan().SequenceEqual(expected))
                    throw new Exception("Base64 transport changed Java RMANVID1 bytes");
                actual.Position=0;var decodedReader=new OwnedVideoReader(actual);
                await decodedReader.Header(CancellationToken.None);int count=0;
                while(await decodedReader.Packet(CancellationToken.None) is not null)count++;
                if(count!=packets)throw new Exception("Base64 transport changed packet count");
                Console.WriteLine("PASS actual Java Base64 fixture after LF-to-CRLF boundary; exact bytes and production wire packets retained");
            }
        }
    }
}
