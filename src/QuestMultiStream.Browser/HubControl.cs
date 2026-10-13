using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestMultiStream.Browser;

// A projection adapter. Scrcpy and browser decoding never populate app peer evidence.
public sealed record HubObservation(string State, DateTimeOffset ObservedUtc, DateTimeOffset ExpiresUtc,
    string Receipt, string? Session = null, bool Handshake = false, bool SocketOpen = false,
    long Frames = 0, DateTimeOffset? FrameUtc = null, long PreviousFrames = 0, DateTimeOffset? PreviousFrameUtc = null);
public sealed record HubEvidence(string Schema, string Owner, string Generation,
    Dictionary<string, HubObservation> Observations);
public sealed record HubAction(string Action, string Direction, string Generation, string RequestId, bool Execute = false);
public sealed record HubActionResult(string State, string Action, string Direction, string Generation,
    string RequestId, bool Effects, string Readback);
public enum HubObservationProvenance { Recorded, SourceOwnerValidated }

public sealed class HubControl
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new StrictUtcDateConverter() }
    };
    private static readonly string[] Lanes = ["Q.device", "Q.control", "W.device", "W.control", "Q→W.peer", "W→Q.peer"];
    private readonly string? evidencePath;private readonly HubProjectionSource? ownerSource;
    private readonly Dictionary<string, (HubAction Request, HubActionResult Result)> requests = [];
    private readonly object sync = new();
    public HubControl(string? evidencePath,HubProjectionSource? ownerSource=null) {
        if (evidencePath is not null && (!Path.IsPathFullyQualified(evidencePath) || !File.Exists(evidencePath)))
            throw new InvalidDataException("Hub evidence must be an existing operator-selected absolute file");
        this.evidencePath = evidencePath;
        this.ownerSource=ownerSource;
    }
    private HubEvidence? Read() {
        if(ownerSource is not null)return ownerSource.Read();
        if (evidencePath is null) return null;
        using var file = File.Open(evidencePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (file.Length > 65536) throw new InvalidDataException("Hub evidence exceeds 64 KiB");
        var evidence = JsonSerializer.Deserialize<HubEvidence>(file, Json) ?? throw new InvalidDataException("Empty hub evidence");
        if (evidence.Schema != "questms.hub.observations.v1" || string.IsNullOrWhiteSpace(evidence.Owner) ||
            string.IsNullOrWhiteSpace(evidence.Generation) || evidence.Observations is null ||
            evidence.Observations.Keys.Any(k => !Lanes.Contains(k))) throw new InvalidDataException("Invalid hub evidence contract");
        return evidence;
    }
    public object Status(DateTimeOffset now, bool commandTransportSelected=false, string[]? requestActions=null) {
        HubEvidence? evidence = null; string? error = null;
        try { evidence = Read(); } catch (Exception e) when (e is IOException or JsonException or InvalidDataException or ArgumentException or FormatException or InvalidOperationException or System.ComponentModel.Win32Exception) { error = e.Message; }
        // Only the successful selected projection path earns this provenance.
        // Static evidence owner labels and command configuration never do.
        var provenance=ownerSource is not null && evidence is not null && error is null?HubObservationProvenance.SourceOwnerValidated:HubObservationProvenance.Recorded;
        return new {
            mode = ownerSource is null?"candidate · recorded evidence only":"selected owner projection", owner = evidence?.Owner ?? "App owner bridge unavailable",
            generation = evidence?.Generation ?? "offline", commandTransportSelected,
            executionVerified = false, executionReadiness = commandTransportSelected?"Checked by the selected owner when a request is dispatched":"No selected command transport",
            requestActions = commandTransportSelected?requestActions??[]:[],
            ownerObservationAuthenticated = provenance==HubObservationProvenance.SourceOwnerValidated, error,
            observations = Lanes.Select(lane => Project(lane, evidence?.Observations.GetValueOrDefault(lane), now, provenance))
        };
    }
    public static object Project(string lane, HubObservation? value, DateTimeOffset now, HubObservationProvenance provenance=HubObservationProvenance.Recorded) {
        var ownerValidated=provenance==HubObservationProvenance.SourceOwnerValidated;
        var state = "unknown"; string reason = "No app-owner observation";
        double? age = null;
        var advancing = value is not null && value.FrameUtc is not null && value.FrameUtc<=now && value.PreviousFrameUtc is not null && value.PreviousFrameUtc<value.FrameUtc && value.Frames>value.PreviousFrames;
        if (value is not null) {
            age = (now - value.ObservedUtc).TotalSeconds;
            if (age < 0 || value.ExpiresUtc <= value.ObservedUtc || string.IsNullOrWhiteSpace(value.Receipt)) { state = "invalid"; reason = "Observation time, expiry or receipt invalid"; }
            else if (now >= value.ExpiresUtc) { state = "expired"; reason = "Observation expired; current state unknown"; }
            else if (lane.EndsWith(".peer") && value.State == "active" &&
                (!value.Handshake || !value.SocketOpen || string.IsNullOrWhiteSpace(value.Session) || value.Frames <= 0 ||
                 value.FrameUtc is null || value.FrameUtc > now || (now - value.FrameUtc.Value).TotalSeconds > 3 ||
                 value.PreviousFrameUtc is null || value.PreviousFrameUtc >= value.FrameUtc || value.Frames <= value.PreviousFrames)) {
                state = "unproven"; reason = "Active requires session handshake, open socket and recent advancing app frames";
            }
            else if(value.State=="fresh-frames") {
                state=advancing&&(now-value.FrameUtc!.Value).TotalSeconds<=3?"fresh-frames":ownerValidated&&advancing&&value.Handshake&&!string.IsNullOrWhiteSpace(value.Session)?"advancing-reports":"unproven";
                reason=state=="fresh-frames"?"Current frame reports; socket state remains unknown":state=="advancing-reports"?"Frames advanced between accepted reports; current socket state is unknown":"Advancing frame reports unavailable; current socket state is unknown";
            }
            else if (value.State is "active" or "connected" or "disconnected" or "failed" or "waiting") {
                state = value.State; reason = ownerValidated?"Selected owner validated this observation; current connection is observed at report time":"Recorded owner observation; live owner authentication pending";
            } else { state = "invalid"; reason = "Unsupported observation state"; }
        }
        return new { lane, state, reason, ageSeconds = age, expiresUtc = value?.ExpiresUtc, receipt = value?.Receipt,
            session = value?.Session, frames = value?.Frames, frameUtc = value?.FrameUtc,
            frameAgeSeconds = value?.FrameUtc is not null?(double?)(now-value.FrameUtc.Value).TotalSeconds:null,
            framesAdvanced = advancing, socketStatus = value?.SocketOpen is true?"reported-open":"unknown",
            qualification = value is null?"unavailable":ownerValidated?"owner-validated":"recorded" };
    }
    // CLI and HTTP adapters share this bounded handler. No shell or ADB executor is accepted.
    public HubActionResult Prepare(HubAction request) {
        if (request.Action is not "establish" and not "disconnect" || request.Direction is not "Q→W" and not "W→Q" and not "both" ||
            !Guid.TryParseExact(request.RequestId, "D", out _) || string.IsNullOrEmpty(request.Generation) || request.Generation.Length > 128)
            throw new InvalidDataException("Invalid typed action, direction, generation or request ID");
        if (request.Execute) throw new InvalidDataException("App-owner executor unavailable; device effects denied");
        lock (sync) {
            if (requests.TryGetValue(request.RequestId, out var retained)) {
                if (retained.Request != request) throw new InvalidDataException("Request ID replay has different inputs");
                return retained.Result;
            }
            var generation = Read()?.Generation ?? "offline";
            if (request.Generation != generation) throw new InvalidDataException("Owner generation changed; refresh before preparing");
            if (requests.Count >= 256) throw new InvalidDataException("Candidate request capacity reached");
            var result = new HubActionResult("prepared", request.Action, request.Direction, generation, request.RequestId,
                false, "Candidate prepared only. No device operation or runtime acceptance. Owner bridge required.");
            requests.Add(request.RequestId, (request, result));
            return result;
        }
    }
    public static void SelfTest() {
        var hub = new HubControl(null); var id = Guid.NewGuid().ToString("D");
        var request = new HubAction("establish", "both", "offline", id);
        if (hub.Prepare(request) != hub.Prepare(request)) throw new Exception("Candidate replay changed result");
        foreach (var bad in new[] {request with {Execute=true}, request with {Action="shell"}, request with {Generation="changed"}, request with {RequestId="invalid"}}) {
            try { hub.Prepare(bad); throw new Exception("Hub accepted invalid request"); } catch (InvalidDataException) { }
        }
        var now = DateTimeOffset.UtcNow;
        var observation = new HubObservation("active", now.AddSeconds(-1), now.AddSeconds(5), "fixture", "session", true, true, 5, now, 4, now.AddMilliseconds(-500));
        string State(HubObservation o) => JsonSerializer.SerializeToElement(Project("Q→W.peer", o, now)).GetProperty("state").GetString()!;
        if (State(observation) != "active" || State(observation with {Handshake=false}) != "unproven" ||
            State(observation with {ExpiresUtc=now}) != "expired" || State(observation with {ObservedUtc=now.AddSeconds(1)}) != "invalid" ||
            State(observation with {FrameUtc=now.AddSeconds(-4)}) != "unproven") throw new Exception("Hub independent freshness guards failed");
        var delayed=observation with {State="fresh-frames",ObservedUtc=now.AddSeconds(-20),ExpiresUtc=now.AddSeconds(160),SocketOpen=false,Frames=2000,PreviousFrames=1000,FrameUtc=now.AddSeconds(-20),PreviousFrameUtc=now.AddSeconds(-40)};
        JsonElement Delayed(HubObservation o,bool validated)=>JsonSerializer.SerializeToElement(Project("Q→W.peer",o,now,validated?HubObservationProvenance.SourceOwnerValidated:HubObservationProvenance.Recorded));
        if(Delayed(delayed,true).GetProperty("state").GetString()!="advancing-reports"||Delayed(delayed,false).GetProperty("state").GetString()!="unproven"||Delayed(delayed with {Frames=1000},true).GetProperty("state").GetString()!="unproven"||Delayed(delayed with {ExpiresUtc=now},true).GetProperty("state").GetString()!="expired"||Delayed(delayed,true).GetProperty("socketStatus").GetString()!="unknown"||Delayed(delayed,true).GetProperty("frameAgeSeconds").GetDouble()!=20)throw new Exception("Delayed owner reports lost independent progress/expiry/socket semantics");
        var device=JsonSerializer.SerializeToElement(Project("Q.device",delayed with {State="connected"},now,HubObservationProvenance.SourceOwnerValidated));
        if(device.GetProperty("qualification").GetString()!="owner-validated"||device.GetProperty("reason").GetString()!.Contains("authentication pending"))throw new Exception("Authenticated owner observation mislabeled recorded");
        var transport=JsonSerializer.SerializeToElement(hub.Status(now,true,["disconnect"]));
        if(!transport.GetProperty("commandTransportSelected").GetBoolean()||transport.GetProperty("executionVerified").GetBoolean()||transport.GetProperty("ownerObservationAuthenticated").GetBoolean())throw new Exception("Command selection falsely proved execution or owner authentication");
        Console.WriteLine("PASS hub request replay/conflict, effect denial, generation and independent peer freshness guards");
    }
}
