using System.Text.Json;
namespace QuestMultiStream.Browser;

internal static class NativeSquareCaptureTests
{
    public static async Task Run(string? fixture,string? base64Fixture=null)
    {
        NativeCaptureBindingTests.Run();
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
