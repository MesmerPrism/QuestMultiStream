using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;

namespace QuestMultiStream.Browser;

public sealed record CaptureInvocation(string InvocationId,string Generation);
public sealed record CaptureOwnerAction(string Schema,string RequestId,string Action,string SourceId,string ExpectedOwnerEpoch,string SourceBindingSha256,CaptureInvocation? ExpectedInvocation,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? Mode=null);
public sealed record CaptureProducerFacts(bool MetadataValidated,bool ConfigObserved,bool KeyObserved,long Packets,long VideoPackets,string? PreviousPtsUs,string? LastPtsUs,DateTimeOffset? PreviousPacketUtc,DateTimeOffset? LastPacketUtc);
public sealed record CaptureTerminalFacts(int? LocalChildExitCode,bool StdoutEof,bool StderrEof,int? NativeChildExitCode,bool NativeStdoutEof,bool NativeStderrEof,bool RemoteAckMatched,bool ResourcesRetired,string Receipt);
public sealed record CaptureOwnerResult(string Schema,string OwnerEpoch,string RequestId,string Action,string SourceId,string DeviceId,string SourceBindingSha256,string InvocationId,string Generation,string State,bool ActualEffectsCompleted,bool RetirementPending,bool NoDispatchProven,CaptureProducerFacts? Producer,CaptureTerminalFacts? Terminal,string Receipt,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? Mode=null);
public sealed record CaptureCurrent(string Schema,string OwnerEpoch,string SourceId,string DeviceId,string SourceBindingSha256,string? OriginalStartRequestId,string DesiredState,CaptureOwnerResult? Operation,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? Mode=null);
public sealed record CaptureSourceBinding(string SourceId,string DeviceId,string SourceBindingSha256);
public sealed record CaptureOwnerStatus(string Schema,string OwnerEpoch,int ProcessId,long ProcessStartUtcTicks,CaptureSourceBinding[] Sources);
public sealed record CaptureOwnerConfig(string Schema,string OwnerEpoch,string Key,string JournalDirectory);
internal sealed record CaptureInstanceRecord(string Schema,string OwnerEpoch,string SourceId,string DeviceId,string SourceBindingSha256,string StartRequestId,string InvocationId,string Generation,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? Mode=null);
public sealed record CaptureReply(int Status,object Body);

// The seam is deliberately a single owned invocation, not a generic command
// executor. Production delegates to the existing producer and fanout unchanged.
internal interface ICaptureInvocation {
    string InvocationId {get;} string Generation {get;} bool Started {get;} bool RetirementProven {get;}
    bool RetirementPending=>Started&&!RetirementProven;
    CaptureProducerFacts Facts(); CaptureTerminalFacts? Terminal {get;}
    Task Run(CaptureFanout fanout,CancellationToken stop);
}
internal sealed class SelectedCaptureInvocation(OwnedDisplaySelection selection,OwnedSource source):ICaptureInvocation {
    readonly OwnedDisplayProducer producer=new(selection,source);
    public string InvocationId=>producer.InvocationId;public string Generation=>producer.Generation;
    public bool Started=>producer.Started;public bool RetirementProven=>producer.RetirementProven;
    public CaptureProducerFacts Facts()=>producer.Snapshot();public CaptureTerminalFacts? Terminal=>producer.Terminal;
    public Task Run(CaptureFanout fanout,CancellationToken stop)=>producer.Run(fanout,stop);
}

// Durable original-operation control, bounded active RAM, current packet facts
// in RAM only. A process restart never dispatches an old intent automatically.
internal sealed partial class CaptureOwnerService:IDisposable {
    readonly object sync=new();readonly string root;readonly string epoch;readonly OwnedSource[] sources;
    readonly FileStream writer;readonly Dictionary<string,Instance> current=[];readonly List<Instance> retainedAllocations=[];
    readonly Func<OwnedSource,ICaptureInvocation> factory;readonly Func<OwnedSource,Task> guard;
    readonly Func<OwnedSource,string,ICaptureInvocation>? modeFactory;readonly Func<OwnedSource,string,bool>? modeValidation;
    readonly Action<string,CaptureFanout> attach;readonly Action<string,string,bool> changed;
    readonly CancellationToken shutdown;readonly Func<bool> admission;bool storageFailed;
    sealed class Instance(CaptureOwnerAction action,ICaptureInvocation producer,CancellationToken shutdown){
        public readonly CaptureOwnerAction Start=action;public readonly string Mode=StartMode(action);public readonly ICaptureInvocation Producer=producer;
        public readonly CancellationTokenSource Stop=CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        public readonly CaptureFanout Fanout=new();public readonly List<CaptureOwnerAction> Stops=[];
        public string State="queued";public bool Finished;public bool NoDispatch;public bool DispatchEntered;public bool EvidenceFailed;public Task? Work;
        public string Desired="running";
    }
    internal CaptureOwnerService(CaptureOwnerConfig config,OwnedSource[] sources,Func<OwnedSource,ICaptureInvocation> factory,Func<OwnedSource,Task> guard,Action<string,CaptureFanout> attach,Action<string,string,bool> changed,CancellationToken shutdown,Func<bool>? admission=null,Func<OwnedSource,string,ICaptureInvocation>? modeFactory=null,Func<OwnedSource,string,bool>? modeValidation=null){
        if(config.Schema!="questms.capture.owner.v1"||!System.Text.RegularExpressions.Regex.IsMatch(config.OwnerEpoch,"^[a-zA-Z0-9._-]{1,128}$")||!System.Text.RegularExpressions.Regex.IsMatch(config.Key,"^[a-zA-Z0-9_-]{43,256}$")||!Path.IsPathFullyQualified(config.JournalDirectory)||!Directory.Exists(config.JournalDirectory))throw new InvalidDataException("Explicit private capture owner configuration required");
        root=config.JournalDirectory;epoch=config.OwnerEpoch;this.sources=sources;this.factory=factory;this.guard=guard;this.attach=attach;this.changed=changed;this.shutdown=shutdown;this.admission=admission??(()=>true);this.modeFactory=modeFactory;this.modeValidation=modeValidation;
        if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Regular capture journal required");
        writer=File.Open(Path.Combine(root,"WRITER.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try{
            var identity=Path.Combine(root,"OWNER.json");var bindings=sources.Select(s=>new CaptureSourceBinding(s.SourceId,s.DeviceId,s.OwnerBindingRef.Sha256)).ToArray();
            var expected=JsonSerializer.SerializeToUtf8Bytes(new{epoch,sources=bindings},OwnerJson.Options);
            if(File.Exists(identity)){if(!Read(identity).SequenceEqual(expected))throw new InvalidDataException("Capture journal owner/source binding differs");}else Create(identity,expected);
            Directory.CreateDirectory(Path.Combine(root,"operations"));
            // Stream history on recovery; do not keep completed requests in RAM.
            foreach(var directory in Directory.EnumerateDirectories(Path.Combine(root,"operations"))){
                var intent=Path.Combine(directory,"INTENT.json");if(!CanonicalId(Path.GetFileName(directory)))throw new InvalidDataException("Unexpected capture operation directory");
                var action=OwnerJson.Read<CaptureOwnerAction>(Read(intent));var resultPath=Path.Combine(directory,"RESULT.json");
                if(!File.Exists(resultPath))throw new InvalidDataException("Incomplete capture admission; explicit journal reconciliation required");
                var result=Stored(action,directory);
                if(result.RetirementPending){var source=sources.SingleOrDefault(s=>s.SourceId==action.SourceId)??throw new InvalidDataException("Unselected recovered source");
                    // Retained unknown blocks replacement. No PID/source-label adoption.
                    result=result with{State="unknown",ActualEffectsCompleted=false,Receipt="Recovered original; actual invocation retirement requires reconciliation"};Atomic(resultPath,JsonSerializer.SerializeToUtf8Bytes(result,OwnerJson.Options));
                    changed(source.SourceId,"unknown",true);
                }
            }
            RecoverOriginalStops();
        }catch{writer.Dispose();throw;}
    }
    internal const string RawTwoLens="raw-two-lens",SquareLeft="square-left";
    static bool ActionSchemaValid(CaptureOwnerAction action)=>
        action.Schema=="questms.capture.action.v2"?action.Mode is null:
        action.Schema=="questms.capture.action.v4"&&(action.Action=="start"?action.Mode is RawTwoLens or SquareLeft:action.Action=="stop"&&action.Mode is null);
    static string StartMode(CaptureOwnerAction action){
        if(action.Action!="start"||!ActionSchemaValid(action))throw new InvalidDataException("Closed capture Start mode required");
        return action.Mode??RawTwoLens;
    }
    bool ModeAllowed(OwnedSource source,string mode)=>(mode==RawTwoLens||mode==SquareLeft&&modeFactory is not null&&modeValidation is not null)&&(modeValidation?.Invoke(source,mode)??mode==RawTwoLens);
    ICaptureInvocation CreateInvocation(OwnedSource source,string mode)=>modeFactory is null?mode==RawTwoLens?factory(source):throw new InvalidDataException("Mode-aware capture factory required"):modeFactory(source,mode);
    CaptureInstanceRecord InstanceRecord(OwnedSource source,Instance instance)=>new(instance.Start.Schema=="questms.capture.action.v4"?"questms.capture.instance.v2":"questms.capture.instance.v1",epoch,source.SourceId,source.DeviceId,source.OwnerBindingRef.Sha256,instance.Start.RequestId,instance.Producer.InvocationId,instance.Producer.Generation,instance.Start.Schema=="questms.capture.action.v4"?instance.Mode:null);
    public CaptureOwnerStatus Status(){using var process=System.Diagnostics.Process.GetCurrentProcess();return new("questms.capture.owner-status.v1",epoch,process.Id,process.StartTime.ToUniversalTime().Ticks,sources.Select(s=>new CaptureSourceBinding(s.SourceId,s.DeviceId,s.OwnerBindingRef.Sha256)).ToArray());}
    string DirectoryFor(string id){if(!CanonicalId(id))throw new InvalidDataException("Canonical capture request ID required");return Path.Combine(root,"operations",id);}
    internal static bool CanonicalId(string id)=>Guid.TryParseExact(id,"D",out var value)&&value.ToString("D")==id;
    public CaptureReply Original(string id){lock(sync){if(OptionalRecord(Path.Combine(OriginalStopDirectory(id),"INTENT.json")) is not null)return OriginalStop(id);var directory=DirectoryFor(id);if(!File.Exists(Path.Combine(directory,"INTENT.json")))return new(404,new{error="Original absent; absence never authorizes replay"});var action=OwnerJson.Read<CaptureOwnerAction>(Read(Path.Combine(directory,"INTENT.json")));var stored=Stored(action,directory);if(current.TryGetValue(action.SourceId,out var instance)&&(instance.Start.RequestId==id||instance.Stops.Any(s=>s.RequestId==id)))return new(200,Result(action,instance));return new(200,stored);}}
    public CaptureReply Current(string sourceId){lock(sync){var source=sources.SingleOrDefault(s=>s.SourceId==sourceId);if(source is null)return new(404,new{error="Source not selected"});if(current.TryGetValue(sourceId,out var instance))return new(200,new CaptureCurrent(instance.Start.Schema=="questms.capture.action.v4"?"questms.capture.current.v3":"questms.capture.current.v2",epoch,sourceId,source.DeviceId,source.OwnerBindingRef.Sha256,instance.Start.RequestId,instance.Desired,Result(instance.Start,instance),instance.Start.Schema=="questms.capture.action.v4"?instance.Mode:null));
        var unknown=Unresolved(sourceId);string? startId=null;if(unknown is not null)startId=OwnerJson.Read<CaptureInstanceRecord>(Read(Path.Combine(DirectoryFor(unknown.RequestId),"INSTANCE.json"))).StartRequestId;
        var operation=startId is null?null:(CaptureOwnerResult)Original(startId).Body;
        return new(200,new CaptureCurrent(operation?.Mode is not null?"questms.capture.current.v3":"questms.capture.current.v2",epoch,sourceId,source.DeviceId,source.OwnerBindingRef.Sha256,startId,"unknown",operation,operation?.Mode));}}
    CaptureOwnerResult? Unresolved(string source){foreach(var directory in Directory.EnumerateDirectories(Path.Combine(root,"operations"))){var intent=Path.Combine(directory,"INTENT.json");var action=OwnerJson.Read<CaptureOwnerAction>(Read(intent));var result=Stored(action,directory);if(result.SourceId==source&&result.RetirementPending)return result;}return null;}
    CaptureOwnerResult Stored(CaptureOwnerAction action,string directory){
        var record=OwnerJson.Read<CaptureInstanceRecord>(Read(Path.Combine(directory,"INSTANCE.json")));var result=OwnerJson.Read<CaptureOwnerResult>(Read(Path.Combine(directory,"RESULT.json")));
        var source=sources.SingleOrDefault(s=>s.SourceId==action.SourceId)??throw new InvalidDataException("Stored source is not selected");
        var start=action.Action=="start"?action:OwnerJson.Read<CaptureOwnerAction>(Read(Path.Combine(DirectoryFor(record.StartRequestId),"INTENT.json")));
        var mode=StartMode(start);var tagged=start.Schema=="questms.capture.action.v4";
        if(action.Action=="stop"&&record!=OwnerJson.Read<CaptureInstanceRecord>(Read(Path.Combine(DirectoryFor(record.StartRequestId),"INSTANCE.json"))))throw new InvalidDataException("Stop instance differs from original Start");
        if(!ActionSchemaValid(action)||action.ExpectedOwnerEpoch!=epoch||action.RequestId!=Path.GetFileName(directory)||!CanonicalId(action.RequestId)||action.Action is not ("start" or "stop")||action.SourceBindingSha256!=source.OwnerBindingRef.Sha256||
            record.Schema!=(tagged?"questms.capture.instance.v2":"questms.capture.instance.v1")||record.Mode!=(tagged?mode:null)||start.Action!="start"||start.ExpectedOwnerEpoch!=epoch||start.SourceId!=source.SourceId||start.SourceBindingSha256!=source.OwnerBindingRef.Sha256||start.RequestId!=record.StartRequestId||record.OwnerEpoch!=epoch||record.SourceId!=source.SourceId||record.DeviceId!=source.DeviceId||record.SourceBindingSha256!=source.OwnerBindingRef.Sha256||!CanonicalId(record.StartRequestId)||
            action.Action=="start"&&(record.StartRequestId!=action.RequestId||action.ExpectedInvocation is not null)||action.Action=="stop"&&(action.ExpectedInvocation!=new CaptureInvocation(record.InvocationId,record.Generation))||
            result.Schema!=((tagged||action.Schema=="questms.capture.action.v4")?"questms.capture.result.v3":"questms.capture.result.v2")||result.Mode!=((tagged||action.Schema=="questms.capture.action.v4")?mode:null)||result.OwnerEpoch!=epoch||result.RequestId!=action.RequestId||result.Action!=action.Action||result.SourceId!=source.SourceId||result.DeviceId!=source.DeviceId||result.SourceBindingSha256!=source.OwnerBindingRef.Sha256||result.InvocationId!=record.InvocationId||result.Generation!=record.Generation||
            result.State is not ("queued" or "running" or "streaming" or "stopping" or "retired" or "failed" or "unknown"))throw new InvalidDataException("Capture original/instance/result join differs");
        if(!result.RetirementPending){var t=result.Terminal;var terminal=t is not null&&t.LocalChildExitCode==0&&t.StdoutEof&&t.StderrEof&&t.NativeChildExitCode==0&&t.NativeStdoutEof&&t.NativeStderrEof&&t.RemoteAckMatched&&t.ResourcesRetired;
            if(!terminal&&(!result.NoDispatchProven||File.Exists(Path.Combine(DirectoryFor(record.StartRequestId),"DISPATCH.json")))||result.NoDispatchProven&&t is not null||result.State is not ("retired" or "failed"))throw new InvalidDataException("Stored retirement lacks exact terminal/no-dispatch proof");
            if(result.NoDispatchProven){var proof=OwnerJson.Read<CaptureInstanceRecord>(Read(Path.Combine(DirectoryFor(record.StartRequestId),"NO_DISPATCH.json")));if(proof!=record)throw new InvalidDataException("Negative dispatch proof instance differs");}
        }
        return result;
    }
    public CaptureReply Submit(CaptureOwnerAction action){lock(sync){
        if(!ActionSchemaValid(action)||!CanonicalId(action.RequestId)||action.Action is not ("start" or "stop")||action.ExpectedOwnerEpoch!=epoch)throw new InvalidDataException("Closed exact capture action required");
        var source=sources.SingleOrDefault(s=>s.SourceId==action.SourceId);if(source is null||action.SourceBindingSha256!=source.OwnerBindingRef.Sha256)throw new InvalidDataException("Capture source binding changed");
        if(action.Action=="start"&&action.ExpectedInvocation is not null||action.Action=="stop"&&(action.ExpectedInvocation is null||!System.Text.RegularExpressions.Regex.IsMatch(action.ExpectedInvocation.InvocationId,"^[0-9a-f]{32}$")||!System.Text.RegularExpressions.Regex.IsMatch(action.ExpectedInvocation.Generation,"^[0-9a-f]{32}$")))throw new InvalidDataException("Exact invocation required only for Stop");
        var directory=DirectoryFor(action.RequestId);var intent=Path.Combine(directory,"INTENT.json");
        if(OptionalRecord(Path.Combine(OriginalStopDirectory(action.RequestId),"INTENT.json")) is not null)return new(409,new{error="ID belongs to original-target Stop"});
        if(File.Exists(intent)){var original=OwnerJson.Read<CaptureOwnerAction>(Read(intent));return original==action?Original(action.RequestId):new(409,new{error="Original capture request differs"});}
        if(storageFailed)return new(503,new{error="Capture journal unavailable; no new dispatch"});
        if(action.Action=="start"&&!ModeAllowed(source,StartMode(action)))return new(409,new{error="Capture mode not admitted for selected source"});
        if(action.Action=="start"&&Tombstone(action.RequestId) is {} cancelled)return RetainCancelledStart(action,source,cancelled);
        current.TryGetValue(source.SourceId,out var instance);
        if(action.Action=="start"){
            if(shutdown.IsCancellationRequested||!admission())return new(410,new{error="Capture owner no longer admits Start"});
            if(instance is not null&&!instance.Finished||Unresolved(source.SourceId)is not null)return new(409,new{error="Original producer remains active or unresolved"});
            var producer=CreateInvocation(source,StartMode(action));instance=new(action,producer,shutdown);
        }else{
            if(instance is null||instance.Producer.InvocationId!=action.ExpectedInvocation!.InvocationId||instance.Producer.Generation!=action.ExpectedInvocation.Generation)return new(409,new{error="Stop targets another or unreconciled invocation"});
            if(instance.Stops.Count>=8)return new(409,new{error="Bounded unresolved Stop queue"});
        }
        try{
            Directory.CreateDirectory(directory);Create(intent,JsonSerializer.SerializeToUtf8Bytes(action,OwnerJson.Options));
            Create(Path.Combine(directory,"INSTANCE.json"),JsonSerializer.SerializeToUtf8Bytes(InstanceRecord(source,instance!),OwnerJson.Options));
            // The immutable intent and allocated invocation precede any Task or
            // cancellation. Failed storage never becomes implicit permission.
            Create(Path.Combine(directory,"RESULT.json"),JsonSerializer.SerializeToUtf8Bytes(Result(action,instance!),OwnerJson.Options));
            if(action.Action=="start"){
                Create(Path.Combine(directory,"DESIRED.json"),JsonSerializer.SerializeToUtf8Bytes(new{desired="running"},OwnerJson.Options));
                current[source.SourceId]=instance!;attach(source.SourceId,instance!.Fanout);changed(source.SourceId,"queued",true);
                instance.Work=Task.Run(()=>Run(source,instance));
            }else{instance!.Stops.Add(action);Atomic(Path.Combine(DirectoryFor(instance.Start.RequestId),"DESIRED.json"),JsonSerializer.SerializeToUtf8Bytes(new{desired="stopped"},OwnerJson.Options));instance.Desired="stopped";if(!instance.Finished){instance.State="stopping";Save(instance);instance.Stop.Cancel();changed(source.SourceId,"stopping",true);}else{Save(instance);instance.Stops.Clear();}}
            return new(202,Result(action,instance!));
        }catch(IOException){storageFailed=true;return new(503,new{error="Original intent may be retained; reconcile this ID, never replay"});}
    }}
    async Task Run(OwnedSource source,Instance instance){Exception? failure=null;try{
        await guard(source);instance.Stop.Token.ThrowIfCancellationRequested();
        lock(sync){instance.Stop.Token.ThrowIfCancellationRequested();if(instance.Desired!="running"||!current.TryGetValue(source.SourceId,out var selected)||!ReferenceEquals(selected,instance))throw new OperationCanceledException("Exact invocation no longer selected for dispatch");if(storageFailed)throw new IOException("Journal held before dispatch");if(!ModeAllowed(source,instance.Mode))throw new InvalidDataException("Capture mode no longer admitted before dispatch");instance.DispatchEntered=true;Create(Path.Combine(DirectoryFor(instance.Start.RequestId),"DISPATCH.json"),JsonSerializer.SerializeToUtf8Bytes(instance.Start.Schema=="questms.capture.action.v4"?(object)new{instance.Producer.InvocationId,instance.Producer.Generation,mode=instance.Mode}:new{instance.Producer.InvocationId,instance.Producer.Generation},OwnerJson.Options));instance.State="running";Save(instance);}
        await instance.Producer.Run(instance.Fanout,instance.Stop.Token);
    }catch(Exception error){failure=error;}
    finally{lock(sync){instance.NoDispatch=!instance.DispatchEntered&&!instance.Producer.RetirementPending;instance.Finished=true;
        var held=instance.Producer.RetirementPending||!instance.NoDispatch&&!instance.Producer.RetirementProven;
        instance.State=held?"unknown":instance.NoDispatch?"failed":failure is null?"retired":"failed";
        try{if(instance.NoDispatch)Create(Path.Combine(DirectoryFor(instance.Start.RequestId),"NO_DISPATCH.json"),Read(Path.Combine(DirectoryFor(instance.Start.RequestId),"INSTANCE.json")));Save(instance);instance.Stops.Clear();}catch(Exception){storageFailed=true;instance.EvidenceFailed=true;instance.State="unknown";held=true;}
        instance.Fanout.Complete(failure);changed(source.SourceId,instance.State,held);instance.Stop.Dispose();
    }}}
    CaptureOwnerResult Result(CaptureOwnerAction action,Instance instance){var source=sources.Single(s=>s.SourceId==action.SourceId);var facts=instance.Producer.Facts();var live=!instance.Finished;var state=instance.State;
        if(live&&state=="running"&&facts.MetadataValidated&&facts.ConfigObserved&&facts.KeyObserved&&facts.VideoPackets>=2&&facts.PreviousPtsUs!=facts.LastPtsUs)state="streaming";
        var absent=instance.Finished&&!instance.EvidenceFailed&&!instance.Producer.RetirementPending&&(instance.NoDispatch||instance.Producer.RetirementProven);if(action.Action=="stop"&&absent)state="retired";if(instance.EvidenceFailed||instance.Finished&&instance.Producer.RetirementPending)state="unknown";
        var complete=action.Action=="start"?state=="streaming":state=="retired"&&absent;
        var tagged=instance.Start.Schema=="questms.capture.action.v4"||action.Schema=="questms.capture.action.v4";
        return new(tagged?"questms.capture.result.v3":"questms.capture.result.v2",epoch,action.RequestId,action.Action,source.SourceId,source.DeviceId,source.OwnerBindingRef.Sha256,instance.Producer.InvocationId,instance.Producer.Generation,state,complete,!absent,instance.NoDispatch&&!instance.EvidenceFailed&&!instance.Producer.RetirementPending,facts,instance.Producer.Terminal,instance.NoDispatch?"Original intent retained; known no producer dispatch":Path.Combine(DirectoryFor(action.RequestId),"RESULT.json"),tagged?instance.Mode:null);
    }
    void Save(Instance instance){foreach(var action in new[]{instance.Start}.Concat(instance.Stops))Atomic(Path.Combine(DirectoryFor(action.RequestId),"RESULT.json"),JsonSerializer.SerializeToUtf8Bytes(Result(action,instance),OwnerJson.Options));}
    static byte[] Read(string path){using var file=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read);if(file.Length>65536||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Regular bounded capture journal record required");var bytes=new byte[file.Length];file.ReadExactly(bytes);return bytes;}
    internal static void Create(string path,byte[] bytes){if(bytes.Length>65536)throw new InvalidDataException("Capture record exceeds bound");using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);file.Write(bytes);file.Flush(true);}
    internal static void Atomic(string path,byte[] bytes){var pending=path+"."+Guid.NewGuid().ToString("N")+".tmp";Create(pending,bytes);File.Move(pending,path,true);}
    public void Dispose(){lock(sync){if(current.Values.Concat(retainedAllocations).Any(i=>i.EvidenceFailed||!i.Finished||i.Producer.RetirementPending||!i.NoDispatch&&!i.Producer.RetirementProven))throw new InvalidOperationException("Actual display ownership/evidence remains unresolved; writer lock retained");writer.Dispose();}}
}
