using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuestMultiStream.Browser;

// MIT implementation of the documented RMANVID1v1 wire; Hostess/Quest remain
// separately licensed, out-of-process producers. No producer implementation is copied.
public sealed class OwnedVideoReader(Stream stream,string? invocationId=null,string? generation=null)
{
    public const int MaxPayload=CaptureFanout.MaximumQueuedBytes-12;
    bool configured,key;ulong? previous;
    public async Task<object> Header(CancellationToken token) {
        var bytes=new byte[32];await stream.ReadExactlyAsync(bytes,token);
        uint At(int offset)=>BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
        if(!bytes.AsSpan(0,8).SequenceEqual("RMANVID1"u8)||At(8)!=1||At(12)!=1||At(24)!=0||At(16) is <1 or >16384||At(20) is <1 or >16384||At(28)>CaptureFanout.MaximumConfigBytes)throw new InvalidDataException("Unsupported owned video header");
        var metadata=new byte[At(28)];await stream.ReadExactlyAsync(metadata,token);
        using var document=JsonDocument.Parse(metadata);if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Owned metadata must be an object");
        if(invocationId is not null&&(!document.RootElement.TryGetProperty("invocation_id",out var invocation)||invocation.GetString()!=invocationId||!document.RootElement.TryGetProperty("generation",out var incarnation)||incarnation.GetString()!=generation))throw new InvalidDataException("Owned media incarnation differs");
        return new {kind="video",Width=At(16),Height=At(20),codec="h264",sourceKind="owned-rmanvid1-v1",captureMode=NativeCaptureSelection.Raw,frameLayout="stereo-sbs"};
    }
    public async Task<byte[]?> Packet(CancellationToken token) {
        var bytes=new byte[32];if(await stream.ReadAsync(bytes.AsMemory(0,1),token)==0)return null;
        await stream.ReadExactlyAsync(bytes.AsMemory(1),token);
        var pts=BinaryPrimitives.ReadUInt64BigEndian(bytes);var flags=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8));var length=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12));
        var config=(flags&2)!=0;var isKey=(flags&1)!=0;
        if(pts>9007199254740991||flags>3||length is <1 or >MaxPayload||config&&length>CaptureFanout.MaximumConfigBytes-12)throw new InvalidDataException("Owned packet bounds/flags differ");
        if(config){configured=true;key=false;previous=null;}
        else {if(!configured||!key&&!isKey||previous is {} last&&pts<=last)throw new InvalidDataException("Owned config/keyframe/timestamp order differs");key|=isKey;previous=pts;}
        var packet=new byte[12+length];BinaryPrimitives.WriteUInt64BigEndian(packet,pts|(config?1UL<<63:0)|(isKey?1UL<<62:0));BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8),length);
        await stream.ReadExactlyAsync(packet.AsMemory(12),token);return packet;
    }
}
public sealed record OwnedPin(string Path,long Bytes,string Sha256);
public sealed record OwnedSource(string SourceId,string DeviceId,string Label,OwnedPin OwnerBindingRef);
public sealed record OwnedDisplaySelection(string Schema,OwnedPin Python,OwnedPin Script,string OutputDirectory,OwnedSource[] Sources)
{
    public void Validate() {
        if(Schema!="questms.display.selection.v1"||Python is null||Script is null||!Path.IsPathFullyQualified(OutputDirectory)||!Directory.Exists(OutputDirectory)||Sources is null||Sources.Length is <1 or >16||
            Sources.Any(s=>s is null||!System.Text.RegularExpressions.Regex.IsMatch(s.SourceId,"^[a-zA-Z0-9_-]{1,64}$")||string.IsNullOrWhiteSpace(s.DeviceId)||string.IsNullOrWhiteSpace(s.Label)||s.OwnerBindingRef is null)||Sources.Select(s=>s.SourceId).Distinct().Count()!=Sources.Length||Sources.Select(s=>s.DeviceId).Distinct().Count()!=Sources.Length)throw new InvalidDataException("Closed owned display selection required");
        foreach(var pin in new[]{Python,Script}.Concat(Sources.Select(s=>s.OwnerBindingRef)))using(Hold(pin)){}
        if((File.GetAttributes(OutputDirectory)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Owned output cannot be a reparse point");
    }
    internal static FileStream Hold(OwnedPin pin) {
        if(!Path.IsPathFullyQualified(pin.Path)||pin.Bytes<=0||pin.Sha256 is null||pin.Sha256.Length!=64||(File.GetAttributes(pin.Path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Exact regular producer source pin required");
        var file=File.Open(pin.Path,FileMode.Open,FileAccess.Read,FileShare.Read);
        try{if(file.Length!=pin.Bytes||Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant()!=pin.Sha256)throw new InvalidDataException("Owned producer source changed");file.Position=0;return file;}catch{file.Dispose();throw;}
    }
}
public sealed record OwnedTerminal(string Schema,string SourceId,string InvocationId,string Generation,string StopNonce,string State,
    int NativeChildExitCode,bool NativeStdoutEof,bool NativeStderrEof,bool RemoteAckMatched,bool ResourcesRetired,string Receipt);
public sealed class OwnedDisplayProducer(OwnedDisplaySelection selected,OwnedSource source)
{
    readonly object factsLock=new();bool metadataValidated,configObserved,keyObserved;long videoPackets;
    string? previousPtsUs;DateTimeOffset? previousPacketUtc;CaptureTerminalFacts? terminalFacts;
    public CaptureTerminalFacts? Terminal {get{lock(factsLock)return terminalFacts;}}
    public CaptureProducerFacts Snapshot(){lock(factsLock)return new(metadataValidated,configObserved,keyObserved,Packets,videoPackets,previousPtsUs,LastPtsUs,previousPacketUtc,LastPacketUtc);}
    public bool Started {get;private set;}
    public bool RetirementProven {get;private set;}
    public string InvocationId {get;}=Guid.NewGuid().ToString("N");
    public string Generation {get;}=Guid.NewGuid().ToString("N");
    string StopNonce {get;}=Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    public long Packets {get;private set;}
    public string? LastPtsUs {get;private set;}
    public DateTimeOffset? LastPacketUtc {get;private set;}
    public static void ValidateTerminal(OwnedTerminal value,string source,string invocation,string generation,string nonce,int localExit,bool stdoutEof,bool stderrEof) {
        if(value.Schema!="questms.display.terminal.v1"||value.SourceId!=source||value.InvocationId!=invocation||value.Generation!=generation||value.StopNonce!=nonce||value.State!="retired"||
            value.NativeChildExitCode!=0||!value.NativeStdoutEof||!value.NativeStderrEof||!value.RemoteAckMatched||!value.ResourcesRetired||string.IsNullOrWhiteSpace(value.Receipt)||localExit!=0||!stdoutEof||!stderrEof)
            throw new InvalidDataException("Exact display retirement/EOF is unproven; source remains held");
    }
    public async Task Run(CaptureFanout fanout,CancellationToken stop) {
        stop.ThrowIfCancellationRequested();
        selected.Validate();if(!selected.Sources.Contains(source))throw new InvalidDataException("Unselected owned display source");
        using var python=OwnedDisplaySelection.Hold(selected.Python);using var script=OwnedDisplaySelection.Hold(selected.Script);using var binding=OwnedDisplaySelection.Hold(source.OwnerBindingRef);
        var output=Path.Combine(selected.OutputDirectory,InvocationId);if(Directory.Exists(output)||File.Exists(output))throw new InvalidDataException("Fresh display namespace required");
        var start=new ProcessStartInfo(selected.Python.Path){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"-B",selected.Script.Path,"display-stream-qms-v1"})start.ArgumentList.Add(arg);
        using var child=Process.Start(start)??throw new IOException("Owned producer did not start");Started=true;
        child.StandardInput.NewLine="\n";
        var diagnostics=Drain(child.StandardError);var reader=new OwnedVideoReader(child.StandardOutput.BaseStream,InvocationId,Generation);
        // Control does not cancel the media read or drop process ownership. Exact
        // remote STOP closes the owned write; genuine stream EOF ends the pump.
        Exception? failure=null,controlFailure=null;bool stdoutEof=false;Task control=Task.CompletedTask;
        try{
            await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {schema="questms.display.start.v1",sourceId=source.SourceId,invocationId=InvocationId,generation=Generation,stopNonce=StopNonce,ownerBindingRef=source.OwnerBindingRef,outputDirectory=output},OwnerJson.Options));await child.StandardInput.FlushAsync();
            control=SendStop();fanout.SetMetadata(await reader.Header(CancellationToken.None));lock(factsLock)metadataValidated=true;
            while(await reader.Packet(CancellationToken.None) is {} packet){
                var stamp=BinaryPrimitives.ReadUInt64BigEndian(packet);lock(factsLock){Packets++;if((stamp&(1UL<<63))!=0){configObserved=true;keyObserved=false;previousPtsUs=null;LastPtsUs=null;previousPacketUtc=null;LastPacketUtc=null;videoPackets=0;}else{keyObserved|=(stamp&(1UL<<62))!=0;videoPackets++;previousPtsUs=LastPtsUs;previousPacketUtc=LastPacketUtc;LastPtsUs=(stamp&((1UL<<62)-1)).ToString();LastPacketUtc=DateTimeOffset.UtcNow;}}fanout.Publish(packet);
            }stdoutEof=true;
        }
        catch(Exception error){failure=error;await RequestStop();try{await child.StandardOutput.BaseStream.CopyToAsync(Stream.Null);stdoutEof=true;}catch{} }
        await child.WaitForExitAsync();await control;var stderrBytes=await diagnostics;
        var terminalPath=Path.Combine(output,"TERMINAL.json");using var terminal=File.Open(terminalPath,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(terminal.Length>4096)throw new InvalidDataException("Display terminal exceeds bound");var bytes=new byte[terminal.Length];await terminal.ReadExactlyAsync(bytes);
        using(var proof=File.Open(Path.Combine(output,"QMS_CHILD.json"),FileMode.CreateNew,FileAccess.Write,FileShare.Read)){JsonSerializer.Serialize(proof,new{schema="questms.display.child-eof.v1",sourceId=source.SourceId,invocationId=InvocationId,generation=Generation,childPid=child.Id,localChildExitCode=child.ExitCode,stdoutEof,stderrEof=true,stderrCharacters=stderrBytes,packets=Packets,lastPtsUs=LastPtsUs,terminal=new{path=terminalPath,bytes=bytes.Length,sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}},OwnerJson.Options);proof.Flush(true);}
        var actualTerminal=OwnerJson.Read<OwnedTerminal>(bytes);
        if(actualTerminal.Schema=="questms.display.terminal.v1"&&actualTerminal.SourceId==source.SourceId&&actualTerminal.InvocationId==InvocationId&&actualTerminal.Generation==Generation&&actualTerminal.StopNonce==StopNonce)
            lock(factsLock)terminalFacts=new(child.ExitCode,stdoutEof,true,actualTerminal.NativeChildExitCode,actualTerminal.NativeStdoutEof,actualTerminal.NativeStderrEof,actualTerminal.RemoteAckMatched,actualTerminal.ResourcesRetired,Path.Combine(output,"QMS_CHILD.json"));
        ValidateTerminal(actualTerminal,source.SourceId,InvocationId,Generation,StopNonce,child.ExitCode,stdoutEof,true);
        if(stderrBytes>65536||controlFailure is not null)throw new InvalidDataException("Producer diagnostics/control result unproven");RetirementProven=true;
        if(failure is not null)throw new InvalidDataException("Owned media failed; exact retirement completed",failure);
        // local functions own the one STOP write and always observe its result.
        async Task SendStop(){var signal=Task.Delay(Timeout.Infinite,stop);if(await Task.WhenAny(signal,child.WaitForExitAsync())==signal)await RequestStop();}
        async Task RequestStop(){try{if(Interlocked.Exchange(ref stopSent,1)==0&&!child.HasExited){await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{schema="questms.display.stop.v1",sourceId=source.SourceId,invocationId=InvocationId,generation=Generation,stopNonce=StopNonce},OwnerJson.Options));await child.StandardInput.FlushAsync();}}catch(Exception error){controlFailure=error;}}
    }
    int stopSent;
    static async Task<long> Drain(StreamReader reader){long total=0;var chars=new char[4096];int count;while((count=await reader.ReadAsync(chars))>0)total+=count;return total;}
}
