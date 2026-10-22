using System.Text.Json;

namespace QuestMultiStream.Browser;

public sealed record OwnerMember(string Role,string DeviceId);
public sealed record OwnerPair(string? Generation,string? Session,OwnerMember[] Members);
public sealed record OwnerReadiness(bool CanStart,bool CanStop,string[] ReasonCodes);
public sealed record OwnerActor(string Role,string ProcessEpoch,string NativeGeneration);
public sealed record OwnerNative(string? Session,string? RuntimeGeneration,OwnerActor[] Actors);
public sealed record OwnerDisplay(string SourceId,string DeviceId,string? ProducerInvocationId,string? ProducerGeneration,
    string State,long Packets,string? LastPtsUs,DateTimeOffset? LastPacketUtc,bool RetirementPending,string Receipt);
public sealed record OwnerLiveStatus(string Schema,string OwnerEpoch,long Revision,DateTimeOffset ObservedUtc,DateTimeOffset ExpiresUtc,
    OwnerPair Selection,string Phase,string? ActiveRequestId,bool ReconciliationRequired,OwnerReadiness Readiness,
    OwnerNative Native,Dictionary<string,HubObservation> Observations,OwnerDisplay[] Displays,string Receipt)
{
    public void Validate(string epoch,DateTimeOffset now) {
        if(Schema!="questms.owner.status.v1"||OwnerEpoch!=epoch||Revision<0||ObservedUtc>now||ExpiresUtc<=now||ExpiresUtc<=ObservedUtc||
            now-ObservedUtc>TimeSpan.FromSeconds(10)||string.IsNullOrWhiteSpace(Receipt)||Selection is null||Readiness is null||Native is null||
            Observations is null||Displays is null||Displays.Any(d=>d is null)||Readiness.ReasonCodes is null||Native.Actors is null||Native.Actors.Any(a=>a is null)||Selection.Members is null||Selection.Members.Any(m=>m is null)||
            Phase is not ("unselected" or "preparing" or "starting" or "running" or "stopping" or "stopped" or "reconciling" or "unknown" or "failed"))
            throw new InvalidDataException("Current owner status identity, freshness or shape differs");
        if(Selection.Members.Length!=0 && (Selection.Members.Length!=2||!Selection.Members.Select(m=>m.Role).Order().SequenceEqual(new[]{"Q","W"})||
            Selection.Members.Any(m=>string.IsNullOrWhiteSpace(m.DeviceId))||Selection.Members.Select(m=>m.DeviceId).Distinct().Count()!=2||
            string.IsNullOrWhiteSpace(Selection.Generation)||string.IsNullOrWhiteSpace(Selection.Session)))throw new InvalidDataException("Exact distinct pair required");
        if(Selection.Members.Length==0&&(Phase!="unselected"||Readiness.CanStart||Readiness.CanStop||Selection.Generation is not null||Selection.Session is not null))throw new InvalidDataException("Unselected owner claimed readiness");
        if(ActiveRequestId is not null&&!Guid.TryParseExact(ActiveRequestId,"D",out _)||ReconciliationRequired&&(Readiness.CanStart||Readiness.CanStop))throw new InvalidDataException("Unresolved owner cannot admit another mutation");
        if(Native.Actors.Length!=0&&(Native.Actors.Length!=2||!Native.Actors.Select(a=>a.Role).Order().SequenceEqual(new[]{"Q","W"})||Native.Actors.Any(a=>string.IsNullOrWhiteSpace(a.ProcessEpoch)||string.IsNullOrWhiteSpace(a.NativeGeneration))))throw new InvalidDataException("Native actor identity differs");
        if((Phase=="running"||Observations.Values.Any(o=>o?.State=="active"))&&(Native.Actors.Length!=2||string.IsNullOrWhiteSpace(Native.Session)||string.IsNullOrWhiteSpace(Native.RuntimeGeneration)))throw new InvalidDataException("Running observations require actual native actor/session joins");
        if(Phase is "unknown" or "reconciling" or "unselected"&&(Readiness.CanStart||Readiness.CanStop))throw new InvalidDataException("Unqualified phase cannot admit effects");
        var lanes=new[]{"Q.device","Q.control","W.device","W.control","Q→W.peer","W→Q.peer"};
        if(Observations.Any(p=>!lanes.Contains(p.Key)||p.Value is null||p.Value.Frames<0||p.Value.PreviousFrames<0||p.Value.Frames>9007199254740991||p.Value.PreviousFrames>9007199254740991))throw new InvalidDataException("Unsupported observation lane/counter");
        if(Displays.Length>2||Displays.Select(d=>d.SourceId).Distinct().Count()!=Displays.Length||Displays.Any(d=>
            !Selection.Members.Any(m=>m.DeviceId==d.DeviceId)||string.IsNullOrWhiteSpace(d.SourceId)||string.IsNullOrWhiteSpace(d.Receipt)||
            d.Packets<0||d.Packets>9007199254740991||d.State is not ("absent" or "starting" or "streaming" or "stopping" or "retired" or "failed" or "unknown")||
            d.LastPtsUs is not null&&(!ulong.TryParse(d.LastPtsUs,out var pts)||pts>9007199254740991)))throw new InvalidDataException("Display identity/state differs");
    }
}
public sealed record OwnerDevice(string DeviceId,string Label,bool Ready,string ReasonCode);
public sealed record OwnerDevices(string Schema,string OwnerEpoch,long Revision,DateTimeOffset ObservedUtc,DateTimeOffset ExpiresUtc,OwnerDevice[] Devices)
{
    public void Validate(string epoch,DateTimeOffset now) {
        if(Schema!="questms.owner.devices.v1"||OwnerEpoch!=epoch||Revision<0||ObservedUtc>now||ExpiresUtc<=now||ExpiresUtc<=ObservedUtc||now-ObservedUtc>TimeSpan.FromSeconds(10)||
            Devices is null||Devices.Length>16||Devices.Any(d=>d is null||string.IsNullOrWhiteSpace(d.DeviceId)||string.IsNullOrWhiteSpace(d.Label)||string.IsNullOrWhiteSpace(d.ReasonCode))||Devices.Select(d=>d.DeviceId).Distinct().Count()!=Devices.Length)
            throw new InvalidDataException("Current device inventory invalid");
    }
}
public sealed record OwnerSelectionRequest(string Schema,string RequestId,string OwnerEpoch,long ExpectedRevision,OwnerMember[] Members);

internal static class OwnerJson
{
    public static readonly JsonSerializerOptions Options=new(HubControl.Json){PropertyNameCaseInsensitive=false,RespectRequiredConstructorParameters=true};
    public static T Read<T>(byte[] bytes) {
        using var document=JsonDocument.Parse(bytes);Check(document.RootElement);
        return JsonSerializer.Deserialize<T>(bytes,Options)??throw new InvalidDataException("Missing owner object");
    }
    static void Check(JsonElement value) {
        if(value.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var item in value.EnumerateObject()){if(!names.Add(item.Name))throw new InvalidDataException("Duplicate owner member");Check(item.Value);}}
        else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())Check(item);
    }
}
