using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace QuestMultiStream.Browser;

public sealed record SessionActor(int Pid,
    [property:JsonPropertyName("start_ticks")] long StartTicks, int Uid,
    [property:JsonPropertyName("app_process_generation")] int AppProcessGeneration,
    [property:JsonPropertyName("process_epoch_id")] string ProcessEpochId);
public sealed record MemberSessionSelection(string Schema, string EvidencePath, string KeyHex,
    string Session, int WriterPid, string WriterBirthUtcTicks, ProjectionPin[] SourcePins,
    Dictionary<string,SessionActor> Actors, Dictionary<string,string> NativeProcessGenerations);
public sealed record RetainedSessionReceipt(DateTimeOffset ObservedUtc, DateTimeOffset ExpiresUtc,
    string Receipt, string Session, int AppProcessGeneration, string NativeProcessGeneration,
    string AcceptedLifecycle, bool CurrentAuthoritySupported);
public sealed record MemberSessionPayload(string Schema, Dictionary<string,int> Generation,
    string Session, DateTimeOffset CreatedUtc, Dictionary<string,SessionActor> Actors,
    Dictionary<string,JsonElement> Observations, Dictionary<string,string> NativeProcessGenerations,
    bool CurrentAuthoritySupported, string AuthorityStatus,
    Dictionary<string,RetainedSessionReceipt> RetainedReceiptEvidence);
public sealed record SessionMember(string Peer, int AppProcessGeneration, string ProcessEpoch,
    string NativeProcessGeneration, RetainedSessionReceipt? RetainedEvidence);
public sealed record HubSessionStatus(string Schema, string State, bool PublisherAuthenticated,
    bool CurrentAuthoritySupported, string? Session, SessionMember[] Members, string Message)
{
    public static HubSessionStatus Unavailable() => new("questms.hub.session-status.v1", "unavailable",
        false, false, null, [], "Session evidence unavailable. Current authority and flow are unverified.");
}

// An independently selected read-only consumer. No action bridge or device RPC.
public sealed class HubSessionProjection : IDisposable
{
    private readonly FileStream selectionLock;
    private readonly MemberSessionSelection selection;
    private readonly byte[] key;
    private const string Unsupported = "unsupported_without_authenticated_capture_monotonic_anchor";
    public HubSessionProjection(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("Absolute session selection required");
        selectionLock = File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        key = [];
        try {
            selection = Decode<MemberSessionSelection>(ReadBounded(selectionLock,8192));
            key = Convert.FromHexString(selection.KeyHex);
            if (selection.Schema != "questms.hub.session-selection.v1" || key.Length != 32 ||
                !Path.IsPathFullyQualified(selection.EvidencePath) || string.IsNullOrWhiteSpace(selection.Session) ||
                selection.SourcePins.Length is <1 or >16 || !Members(selection.Actors.Keys) ||
                !Members(selection.NativeProcessGenerations.Keys) || selection.WriterPid <= 0 ||
                !long.TryParse(selection.WriterBirthUtcTicks,out _)) throw new InvalidDataException("Closed session selection required");
            foreach (var peer in new[]{"Q","W"}) ValidateActor(selection.Actors[peer],selection.NativeProcessGenerations[peer]);
            if (selection.SourcePins.Select(p=>Path.GetFullPath(p.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=selection.SourcePins.Length)
                throw new InvalidDataException("Ambiguous session source pins");
            Guard();
        } catch { selectionLock.Dispose(); CryptographicOperations.ZeroMemory(key); throw; }
    }
    private static bool Members(IEnumerable<string> keys) => keys.Order().SequenceEqual(new[]{"Q","W"});
    private static void ValidateActor(SessionActor a,string native)
    {
        if (a is null || a.Pid<=0 || a.StartTicks<=0 || a.Uid<=0 || a.AppProcessGeneration<=0 ||
            string.IsNullOrEmpty(a.ProcessEpochId) || !a.ProcessEpochId.StartsWith($"app-process.{a.AppProcessGeneration}.",StringComparison.Ordinal) ||
            a.ProcessEpochId.Length<=$"app-process.{a.AppProcessGeneration}.".Length ||
            native is null || !Regex.IsMatch(native,"^[1-9][0-9]{0,19}$")) throw new InvalidDataException("Member application/native identity differs");
    }
    private static byte[] ReadBounded(FileStream file,int maximum)
    {
        if (file.Length>maximum) throw new InvalidDataException("Session input exceeds bound");
        using var memory=new MemoryStream(); file.CopyTo(memory);
        if (memory.Length>maximum) throw new InvalidDataException("Session input grew beyond bound");
        return memory.ToArray();
    }
    private static T Decode<T>(byte[] bytes)
    {
        using var doc=JsonDocument.Parse(bytes);
        void Unique(JsonElement e) {
            if(e.ValueKind==JsonValueKind.Object) {
                var seen=new HashSet<string>(StringComparer.Ordinal);
                foreach(var p in e.EnumerateObject()) {if(!seen.Add(p.Name))throw new InvalidDataException("Duplicate session property");Unique(p.Value);}
            } else if(e.ValueKind==JsonValueKind.Array) foreach(var v in e.EnumerateArray())Unique(v);
        }
        Unique(doc.RootElement);
        var options=new JsonSerializerOptions(HubControl.Json){RespectRequiredConstructorParameters=true,RespectNullableAnnotations=true,
            PropertyNameCaseInsensitive=false,NumberHandling=JsonNumberHandling.Strict};
        return JsonSerializer.Deserialize<T>(bytes,options)??throw new InvalidDataException("Missing session input");
    }
    private void Guard()
    {
        using var writer=Process.GetProcessById(selection.WriterPid);
        if(writer.HasExited || writer.StartTime.ToUniversalTime().Ticks.ToString()!=selection.WriterBirthUtcTicks)
            throw new InvalidDataException("Selected session publisher ended or changed");
        foreach(var pin in selection.SourcePins) {
            if(!Path.IsPathFullyQualified(pin.Path) || new FileInfo(pin.Path).Length>1048576)
                throw new InvalidDataException("Bounded publisher source required");
            using var file=File.Open(pin.Path,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant()!=pin.Sha256)
                throw new InvalidDataException("Selected publisher source changed");
        }
    }
    public HubSessionStatus Read()
    {
        Guard();
        using var file=File.Open(selection.EvidencePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        var envelope=Decode<ProjectionEnvelope>(ReadBounded(file,65536));
        var payload=Encoding.UTF8.GetBytes(envelope.Payload);
        if(payload.Length>32768 || !CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key,payload),Convert.FromHexString(envelope.Mac)))
            throw new InvalidDataException("Session projection authentication failed");
        var value=Decode<MemberSessionPayload>(payload);
        var now=DateTimeOffset.UtcNow;
        if(value.Schema!="questms.hub.owner-projection.v2" || value.Session!=selection.Session ||
            value.CreatedUtc>now || (now-value.CreatedUtc).TotalSeconds>10 ||
            !Members(value.Generation.Keys) || !Members(value.Actors.Keys) || !Members(value.NativeProcessGenerations.Keys) ||
            value.CurrentAuthoritySupported || value.AuthorityStatus!=Unsupported || value.Observations.Count!=0 ||
            value.RetainedReceiptEvidence.Keys.Any(p=>p is not "Q" and not "W"))
            throw new InvalidDataException("Session identity or unsupported authority differs");
        var members=new List<SessionMember>();
        foreach(var peer in new[]{"Q","W"}) {
            var actor=value.Actors[peer];var native=value.NativeProcessGenerations[peer];ValidateActor(actor,native);
            if(actor!=selection.Actors[peer] || native!=selection.NativeProcessGenerations[peer] || value.Generation[peer]!=actor.AppProcessGeneration)
                throw new InvalidDataException("Selected member identity changed");
            value.RetainedReceiptEvidence.TryGetValue(peer,out var receipt);
            if(receipt is not null && (receipt.CurrentAuthoritySupported || receipt.Session!=selection.Session ||
                receipt.AppProcessGeneration!=actor.AppProcessGeneration || receipt.NativeProcessGeneration!=native ||
                receipt.AcceptedLifecycle!="active" || receipt.ExpiresUtc<=receipt.ObservedUtc || string.IsNullOrWhiteSpace(receipt.Receipt) || receipt.Receipt.Length>512))
                throw new InvalidDataException("Retained member evidence differs");
            members.Add(new(peer,actor.AppProcessGeneration,actor.ProcessEpochId,native,receipt));
        }
        Guard();
        return new("questms.hub.session-status.v1","retained-evidence",true,false,value.Session,members.ToArray(),
            "Retained owner evidence only. Current authority and peer flow are unavailable.");
    }
    public void Dispose(){selectionLock.Dispose();CryptographicOperations.ZeroMemory(key);}
}
