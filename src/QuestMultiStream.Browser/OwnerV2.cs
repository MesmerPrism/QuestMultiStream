using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace QuestMultiStream.Browser;

public sealed record OwnerCapabilitiesV2(string Schema,string OwnerEpoch,int ProtocolVersion,string[] Actions,string[] Modes)
{
    public void Validate(string epoch){V2.Require(Schema=="questms.owner.capabilities.v2"&&OwnerEpoch==epoch&&ProtocolVersion==2&&Actions is not null&&Modes is not null&&Actions.SequenceEqual(V2.Actions)&&Modes.SequenceEqual(V2.Modes),"Unsupported owner v2 capabilities");}
}
public sealed record OwnerReadinessV2(bool CanEstablish,bool CanNormalStop,bool CanWhole,bool CanMode,string[] ReasonCodes);
public sealed record OwnerNativeFreshnessV2(string State,string? ObservedMonotonicNs,string? ExpiresMonotonicNs)
{
    public void Validate(){V2.Require(State is "unknown" or "retained" or "current" or "stale","Unsupported native freshness");V2.Decimal(ObservedMonotonicNs);V2.Decimal(ExpiresMonotonicNs);if(State=="current")V2.Require(ObservedMonotonicNs is not null&&ExpiresMonotonicNs is not null&&ulong.Parse(ExpiresMonotonicNs)>ulong.Parse(ObservedMonotonicNs),"Native freshness lacks owner-qualified clocks");}
}
public record OwnerPeerV2(string Role,string DeviceId,string? AppProcessGeneration,string? ProcessEpochId,string? NativeGeneration,string? NativeProcessGeneration,
    string? RuntimeConfigId,string? NativeSession,string? RunId,string? Mode,OwnerNativeFreshnessV2 NativeFreshness,string Receipt)
{
    public void Validate(){
        V2.Require(Role is "Q" or "W","Unsupported peer role");V2.Text(DeviceId,128);V2.Text(Receipt,4096);
        V2.Decimal(AppProcessGeneration);V2.Decimal(NativeGeneration);V2.Decimal(NativeProcessGeneration);V2.Opaque(ProcessEpochId);V2.Opaque(NativeSession);
        V2.Hex(RuntimeConfigId,64);V2.Hex(RunId,32);V2.Require(Mode is null or "own" or "peer" or "blend","Unsupported peer mode");
        V2.Require(NativeFreshness is not null,"Missing native freshness");NativeFreshness.Validate();
    }
    internal bool HasActualIdentity=>AppProcessGeneration is not null&&ProcessEpochId is not null&&NativeGeneration is not null&&RuntimeConfigId is not null&&NativeSession is not null&&RunId is not null;
}
[method:JsonConstructor]
public sealed record OwnerPeerResultV2(string Role,string DeviceId,string? AppProcessGeneration,string? ProcessEpochId,string? NativeGeneration,string? NativeProcessGeneration,
    string? RuntimeConfigId,string? NativeSession,string? RunId,string? Mode,OwnerNativeFreshnessV2 NativeFreshness,string Receipt,string State):
    OwnerPeerV2(Role,DeviceId,AppProcessGeneration,ProcessEpochId,NativeGeneration,NativeProcessGeneration,RuntimeConfigId,NativeSession,RunId,Mode,NativeFreshness,Receipt)
{
    public OwnerPeerResultV2(OwnerPeerV2 peer,string state):this(peer.Role,peer.DeviceId,peer.AppProcessGeneration,peer.ProcessEpochId,peer.NativeGeneration,peer.NativeProcessGeneration,peer.RuntimeConfigId,peer.NativeSession,peer.RunId,peer.Mode,peer.NativeFreshness,peer.Receipt,state){}
}
public sealed record OwnerStatusV2(string Schema,string OwnerEpoch,long Revision,DateTimeOffset ObservedUtc,string? SelectionId,OwnerMember[] Members,
    string Phase,string? ActiveRequestId,bool ReconciliationRequired,OwnerReadinessV2 Readiness,OwnerPeerV2[] Peers,string Receipt)
{
    public void Validate(string epoch){
        V2.Require(Schema=="questms.owner.status.v2"&&OwnerEpoch==epoch,"Owner v2 status identity differs");V2.Revision(Revision);V2.Text(Receipt,4096);
        V2.Require(Phase is "unselected" or "preparing" or "starting" or "running" or "stopping" or "stopped" or "reconciling" or "unknown" or "failed","Unsupported owner phase");
        V2.Require(Members is not null&&Peers is not null&&Readiness?.ReasonCodes is not null,"Missing owner state");
        foreach(var reason in Readiness.ReasonCodes)V2.Text(reason,4096);
        V2.Require(Readiness.ReasonCodes.Length<=64,"Reason inventory bound");V2.Id(ActiveRequestId);
        if(Members.Length==0){V2.Require(SelectionId is null&&Phase=="unselected"&&Peers.Length==0,"Unselected owner differs");}
        else{V2.Id(SelectionId,true);V2.Members(Members);}
        if(Peers.Length!=0){V2.Require(Peers.Length==2&&Peers.Select(p=>p?.Role).Order().SequenceEqual(new[]{"Q","W"}),"Exact Q/W peer facts required");foreach(var peer in Peers){peer.Validate();V2.Require(Members.Any(m=>m.Role==peer.Role&&m.DeviceId==peer.DeviceId),"Peer and selected device differ");}}
        if(ReconciliationRequired||Phase is "unknown" or "reconciling" or "unselected")
            V2.Require(!Readiness.CanEstablish&&!Readiness.CanNormalStop&&!Readiness.CanWhole&&!Readiness.CanMode,"Unresolved owner claimed readiness");
        if(Phase=="running")V2.Require(Peers.Length==2&&Peers.All(p=>p.HasActualIdentity),"Running requires real independent peer identities");
    }
}
public sealed record OwnerDevicesV2(string Schema,string OwnerEpoch,long Revision,DateTimeOffset ObservedUtc,OwnerDevice[] Devices)
{
    public void Validate(string epoch){V2.Require(Schema=="questms.owner.devices.v2"&&OwnerEpoch==epoch&&Devices is not null&&Devices.Length<=16,"Owner devices differ");V2.Revision(Revision);foreach(var d in Devices){V2.Require(d is not null,"Missing device");V2.Text(d.DeviceId,128);V2.Text(d.Label,128);V2.Text(d.ReasonCode,4096);}V2.Require(Devices.Select(d=>d.DeviceId).Distinct().Count()==Devices.Length,"Duplicate device");}
}
public sealed record OwnerRunChoiceV2(string RunId,string Label,bool Resumable,string ReasonCode);
public sealed record OwnerRunsV2(string Schema,string OwnerEpoch,long Revision,string? SelectionId,DateTimeOffset ObservedUtc,OwnerRunChoiceV2[] Runs)
{
    public void Validate(string epoch){V2.Require(Schema=="questms.owner.runs.v2"&&OwnerEpoch==epoch&&Runs is not null&&Runs.Length<=64,"Owner runs differ");V2.Revision(Revision);V2.Id(SelectionId);foreach(var run in Runs){V2.Require(run is not null,"Missing run");V2.Hex(run.RunId,32,true);V2.Text(run.Label,128);V2.Text(run.ReasonCode,4096);}V2.Require(Runs.Select(r=>r.RunId).Distinct().Count()==Runs.Length&&(SelectionId is not null||Runs.Length==0),"Duplicate or unselected run choice");}
}
public sealed record OwnerSelectionV2(string Schema,string RequestId,string OwnerEpoch,long ExpectedRevision,OwnerMember[] Members)
{
    public void Validate(string epoch){V2.Require(Schema=="questms.owner.selection.v2"&&OwnerEpoch==epoch,"Selection identity differs");V2.Id(RequestId,true);V2.Revision(ExpectedRevision);V2.Members(Members);}
}
public sealed record OwnerRunRequestV2(string Kind,string? RunId);
public sealed record OwnerActionV2(string Schema,string RequestId,string OwnerEpoch,string SelectionId,long ExpectedRevision,string Action,OwnerRunRequestV2? RunChoice,string? Mode)
{
    public void Validate(string epoch){
        V2.Require(Schema=="questms.owner.action.v2"&&OwnerEpoch==epoch,"Action identity differs");V2.Id(RequestId,true);V2.Id(SelectionId,true);V2.Revision(ExpectedRevision);
        V2.Require(V2.Actions.Contains(Action),"Unsupported product action");
        if(Action=="establish"){V2.Require(RunChoice is not null&&RunChoice.Kind is "new" or "resume","Explicit NewRun/ResumeRun choice required");if(RunChoice.Kind=="new")V2.Require(RunChoice.RunId is null,"NewRun ID belongs to owner");else V2.Hex(RunChoice.RunId,32,true);}
        else V2.Require(RunChoice is null,"Run choice belongs to establish");
        V2.Require(Action is "establish" or "mode"?Mode is "own" or "peer" or "blend":Mode is null,"Explicit closed mode required");
    }
}
public sealed record OwnerResultV2(string Schema,string RequestId,string OwnerEpoch,string SelectionId,string Action,string State,bool ActualEffectsCompleted,OwnerPeerResultV2[] Peers,string Receipt)
{
    public void Validate(string epoch,string requestId){
        V2.Require(Schema=="questms.owner.result.v2"&&OwnerEpoch==epoch&&RequestId==requestId&&V2.Actions.Contains(Action),"Result identity differs");V2.Id(RequestId,true);V2.Id(SelectionId,true);V2.Text(Receipt,4096);
        V2.Require(State is "queued" or "running" or "effective" or "failed" or "unknown","Unsupported result state");
        V2.Require(Peers is not null&&(Peers.Length==0||Peers.Length==2&&Peers.Select(p=>p?.Role).Order().SequenceEqual(new[]{"Q","W"})),"Exact result peer shape required");
        V2.Require(Peers.Select(p=>p.DeviceId).Distinct().Count()==Peers.Length,"Distinct result headsets required");
        foreach(var p in Peers){p.Validate();V2.Require(p.State is "pending" or "effective" or "failed" or "unknown","Unsupported peer result");if(p.State=="effective")V2.Require(p.HasActualIdentity,"Effective peer lacks actual identity");}
        V2.Require(ActualEffectsCompleted==(State=="effective")&&(!ActualEffectsCompleted||Peers.Length==2&&Peers.All(p=>p.State=="effective")),"Effective requires both actual peer results");
    }
}
internal static class V2
{
    internal static readonly string[] Actions=["establish","normalStop","whole","mode"],Modes=["own","peer","blend"];
    internal static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value,string reason){if(!value)throw new InvalidDataException(reason);}
    internal static void Text(string? value,int max)=>Require(!string.IsNullOrWhiteSpace(value)&&value.Length<=max&&!value.Any(char.IsControl),"Bounded text required");
    internal static void Opaque(string? value){if(value is not null)Text(value,256);}
    internal static void Decimal(string? value){if(value is not null)Require(Regex.IsMatch(value,"^(0|[1-9][0-9]{0,19})$")&&ulong.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out _),"Opaque integer must be canonical decimal string");}
    internal static void Hex(string? value,int count,bool required=false){Require(!required||value is not null,"Actual run identity required");if(value is not null)Require(Regex.IsMatch(value,"^[0-9a-f]{"+count+"}$"),"Actual hexadecimal identity differs");}
    internal static void Id(string? value,bool required=false){Require(!required||value is not null,"UUID required");if(value is not null)Require(Guid.TryParseExact(value,"D",out var id)&&id.ToString("D")==value,"Canonical UUID required");}
    internal static void Revision(long value)=>Require(value>=0&&value<=9007199254740991,"Safe revision required");
    internal static void Members(OwnerMember[]? members){Require(members is not null&&members.Length==2&&members.All(m=>m is not null)&&members.Select(m=>m.Role).Order().SequenceEqual(new[]{"Q","W"}),"Exact Q/W selection required");foreach(var m in members!)Text(m.DeviceId,128);Require(members!.Select(m=>m.DeviceId).Distinct().Count()==2,"Distinct headsets required");}
}
