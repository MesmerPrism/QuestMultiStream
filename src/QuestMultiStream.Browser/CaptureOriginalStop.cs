using System.Text.Json;

namespace QuestMultiStream.Browser;

public sealed record CaptureOriginalStopAction(string Schema,string RequestId,string Action,string SourceId,string ExpectedOwnerEpoch,string SourceBindingSha256,string OriginalStartRequestId);
public sealed record CaptureOriginalStopResult(string Schema,string OwnerEpoch,string RequestId,string Action,string SourceId,string DeviceId,string SourceBindingSha256,string OriginalStartRequestId,string State,bool ActualEffectsCompleted,bool RetirementPending,bool NoDispatchProven,CaptureOwnerResult? Target,string Receipt);
internal sealed record CaptureOriginalStopProof(string Schema,string OwnerEpoch,string SourceId,string DeviceId,string SourceBindingSha256,string OriginalStartRequestId,string FirstStopRequestId,bool NoDispatchAtAdmission);

internal sealed partial class CaptureOwnerService {
    string OriginalStopDirectory(string id){if(!CanonicalId(id))throw new InvalidDataException("Canonical original Stop ID required");return Path.Combine(root,"original-stops",id);}
    string TombstonePath(string start)=>Path.Combine(root,"cancelled-originals",start+".json");
    // Use explicit filesystem errors, never File.Exists(false) as negative proof.
    static bool DirectoryAbsent(string path){try{var flags=File.GetAttributes(path);if((flags&FileAttributes.Directory)==0||(flags&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Regular journal directory required");return false;}catch(FileNotFoundException){return true;}catch(DirectoryNotFoundException){return true;}}
    static byte[]? OptionalRecord(string path){try{return Read(path);}catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}}
    CaptureOriginalStopProof? Tombstone(string start){
        var bytes=OptionalRecord(TombstonePath(start));if(bytes is null)return null;
        var proof=OwnerJson.Read<CaptureOriginalStopProof>(bytes);
        var original=OwnerJson.Read<CaptureOriginalStopAction>(Read(Path.Combine(OriginalStopDirectory(proof.FirstStopRequestId),"INTENT.json")));
        var source=sources.SingleOrDefault(s=>s.SourceId==proof.SourceId);
        if(source is null||proof.Schema!="questms.capture.cancel-original-proof.v1"||proof.OwnerEpoch!=epoch||proof.DeviceId!=source.DeviceId||proof.SourceBindingSha256!=source.OwnerBindingRef.Sha256||proof.OriginalStartRequestId!=start||!CanonicalId(start)||original.Schema!="questms.capture.action.v3"||original.Action!="stop"||original.RequestId!=proof.FirstStopRequestId||original.OriginalStartRequestId!=start||original.ExpectedOwnerEpoch!=epoch||original.SourceId!=proof.SourceId||original.SourceBindingSha256!=proof.SourceBindingSha256)throw new InvalidDataException("Original cancellation proof differs");
        return proof;
    }
    void RecoverOriginalStops(){
        Directory.CreateDirectory(Path.Combine(root,"original-stops"));Directory.CreateDirectory(Path.Combine(root,"cancelled-originals"));
        foreach(var directory in Directory.EnumerateDirectories(Path.Combine(root,"original-stops"))){
            var action=OwnerJson.Read<CaptureOriginalStopAction>(Read(Path.Combine(directory,"INTENT.json")));
            if(action.RequestId!=Path.GetFileName(directory)||Tombstone(action.OriginalStartRequestId) is null)throw new InvalidDataException("Incomplete original cancellation; explicit reconciliation required");
            _=OriginalStop(action.RequestId); // Validate every retained scope without replay.
        }
    }
    CaptureReply OriginalStop(string id){
        var directory=OriginalStopDirectory(id);var action=OwnerJson.Read<CaptureOriginalStopAction>(Read(Path.Combine(directory,"INTENT.json")));
        var source=sources.SingleOrDefault(s=>s.SourceId==action.SourceId);var proof=Tombstone(action.OriginalStartRequestId);
        if(source is null||action.Schema!="questms.capture.action.v3"||action.Action!="stop"||action.RequestId!=id||action.ExpectedOwnerEpoch!=epoch||action.SourceBindingSha256!=source.OwnerBindingRef.Sha256||proof is null||proof.SourceId!=action.SourceId||proof.SourceBindingSha256!=action.SourceBindingSha256)throw new InvalidDataException("Original Stop scope differs");
        CaptureOwnerResult? target=null;var absent=proof.NoDispatchAtAdmission;
        if(!DirectoryAbsent(DirectoryFor(action.OriginalStartRequestId))){
            var reply=Original(action.OriginalStartRequestId);target=reply.Status==200?reply.Body as CaptureOwnerResult:null;
            if(target is null||target.Action!="start"||target.SourceId!=source.SourceId||target.SourceBindingSha256!=source.OwnerBindingRef.Sha256)throw new InvalidDataException("Original Stop target differs");
            absent=target.NoDispatchProven;
        }else if(!absent)throw new InvalidDataException("Allocated original target lost; retirement unconfirmed");
        var completed=absent||target is {RetirementPending:false};
        return new(200,new CaptureOriginalStopResult("questms.capture.original-stop-result.v1",epoch,id,"stop",source.SourceId,source.DeviceId,source.OwnerBindingRef.Sha256,action.OriginalStartRequestId,completed?"retired":target?.State=="unknown"?"unknown":"stopping",completed,!completed,absent,target,TombstonePath(action.OriginalStartRequestId)));
    }
    public CaptureReply SubmitOriginalStop(CaptureOriginalStopAction action){lock(sync){
        if(action.Schema!="questms.capture.action.v3"||action.Action!="stop"||!CanonicalId(action.RequestId)||!CanonicalId(action.OriginalStartRequestId)||action.RequestId==action.OriginalStartRequestId||action.ExpectedOwnerEpoch!=epoch)throw new InvalidDataException("Closed original-target Stop required");
        var source=sources.SingleOrDefault(s=>s.SourceId==action.SourceId);if(source is null||action.SourceBindingSha256!=source.OwnerBindingRef.Sha256)throw new InvalidDataException("Original Stop source binding differs");
        var directory=OriginalStopDirectory(action.RequestId);var prior=OptionalRecord(Path.Combine(directory,"INTENT.json"));
        if(prior is not null)return OwnerJson.Read<CaptureOriginalStopAction>(prior)==action?OriginalStop(action.RequestId):new(409,new{error="Original Stop request differs"});
        if(!DirectoryAbsent(DirectoryFor(action.RequestId)))return new(409,new{error="ID belongs to another original"});
        if(OptionalRecord(Path.Combine(OriginalStopDirectory(action.OriginalStartRequestId),"INTENT.json")) is not null)return new(409,new{error="Target ID belongs to a Stop"});
        if(storageFailed)return new(503,new{error="Capture journal held; original Stop unconfirmed"});
        try{
            var proof=Tombstone(action.OriginalStartRequestId);var missing=DirectoryAbsent(DirectoryFor(action.OriginalStartRequestId));
            if(missing&&current.TryGetValue(source.SourceId,out var held)&&held.Start.RequestId==action.OriginalStartRequestId)throw new InvalidDataException("Known allocated original lost journal; absence unproven");
            if(proof is not null&&(proof.SourceId!=source.SourceId||proof.SourceBindingSha256!=source.OwnerBindingRef.Sha256))return new(409,new{error="Original belongs to another source binding"});
            if(!missing){var target=(CaptureOwnerResult)Original(action.OriginalStartRequestId).Body;if(target.Action!="start"||target.SourceId!=source.SourceId||target.SourceBindingSha256!=source.OwnerBindingRef.Sha256)return new(409,new{error="Original target scope differs"});}
            Directory.CreateDirectory(directory);Create(Path.Combine(directory,"INTENT.json"),JsonSerializer.SerializeToUtf8Bytes(action,OwnerJson.Options));
            if(proof is null)Create(TombstonePath(action.OriginalStartRequestId),JsonSerializer.SerializeToUtf8Bytes(new CaptureOriginalStopProof("questms.capture.cancel-original-proof.v1",epoch,source.SourceId,source.DeviceId,source.OwnerBindingRef.Sha256,action.OriginalStartRequestId,action.RequestId,missing),OwnerJson.Options));
            // Tombstone is durable before acknowledgement or exact cancellation.
            // A different rearmed original under the source is never cancelled.
            if(current.TryGetValue(source.SourceId,out var instance)&&instance.Start.RequestId==action.OriginalStartRequestId){
                Atomic(Path.Combine(DirectoryFor(instance.Start.RequestId),"DESIRED.json"),JsonSerializer.SerializeToUtf8Bytes(new{desired="stopped"},OwnerJson.Options));instance.Desired="stopped";
                if(!instance.Finished){instance.State="stopping";Save(instance);instance.Stop.Cancel();changed(source.SourceId,"stopping",true);}
            }
            return OriginalStop(action.RequestId) with{Status=202};
        }catch(Exception error)when(error is IOException or InvalidDataException){storageFailed=true;return new(503,new{error="Original Stop may be retained; GET only, no replay"});}
    }}
    CaptureReply RetainCancelledStart(CaptureOwnerAction action,OwnedSource source,CaptureOriginalStopProof proof){
        if(proof.SourceId!=source.SourceId||proof.SourceBindingSha256!=source.OwnerBindingRef.Sha256)return new(409,new{error="Cancelled original binding differs"});
        // These are actual allocated local invocation IDs, never native receipts.
        // No Task, source current replacement, fanout or producer Run is created.
        var instance=new Instance(action,CreateInvocation(source,StartMode(action)),shutdown){Finished=true,NoDispatch=true,State="failed",Desired="stopped"};var directory=DirectoryFor(action.RequestId);
        if(instance.Producer.RetirementPending){instance.NoDispatch=false;instance.State="unknown";storageFailed=true;retainedAllocations.Add(instance);}
        try{Directory.CreateDirectory(directory);Create(Path.Combine(directory,"INTENT.json"),JsonSerializer.SerializeToUtf8Bytes(action,OwnerJson.Options));
            var record=InstanceRecord(source,instance);
            Create(Path.Combine(directory,"INSTANCE.json"),JsonSerializer.SerializeToUtf8Bytes(record,OwnerJson.Options));if(instance.NoDispatch)Create(Path.Combine(directory,"NO_DISPATCH.json"),JsonSerializer.SerializeToUtf8Bytes(record,OwnerJson.Options));Create(Path.Combine(directory,"DESIRED.json"),JsonSerializer.SerializeToUtf8Bytes(new{desired="stopped"},OwnerJson.Options));Create(Path.Combine(directory,"RESULT.json"),JsonSerializer.SerializeToUtf8Bytes(Result(action,instance),OwnerJson.Options));return new(202,Result(action,instance));
        }catch(IOException){storageFailed=true;return new(503,new{error="Cancelled Start evidence incomplete; GET only"});}finally{instance.Stop.Dispose();}
    }
}
