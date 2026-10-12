using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace QuestMultiStream.Browser;

public sealed record ProjectionPin(string Path,string Sha256);
public sealed record ProjectionActor(string ProcessEpoch,string NativeGeneration);
public sealed record ProjectionSelection(string EvidencePath,string KeyHex,string Generation,string Session,
    int WriterPid,string WriterBirthUtcTicks,ProjectionPin[] SourcePins,Dictionary<string,ProjectionActor> Actors);
public sealed record ProjectionEnvelope(string Payload,string Mac);
public sealed record OwnerProjection(string Schema,string Generation,string Session,DateTimeOffset CreatedUtc,
    Dictionary<string,ProjectionActor> Actors,Dictionary<string,HubObservation> Observations);

// Local owner-authenticated projections. This key authenticates the selected
// publisher, not Manifold signatures or arbitrary JSON. Publisher source must
// join actual accepted owner receipts/raw EOF/validator outputs before signing.
public sealed class HubProjectionSource : IDisposable
{
    private readonly FileStream selectionLock;private readonly ProjectionSelection selection;private readonly byte[] key=[];
    public HubProjectionSource(string path) {
        if(!Path.IsPathFullyQualified(path))throw new InvalidDataException("Absolute projection selection required");
        selectionLock=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        try {
        if(selectionLock.Length>8192)throw new InvalidDataException("Projection selection exceeds bound");
        selection=JsonSerializer.Deserialize<ProjectionSelection>(selectionLock,HubControl.Json)??throw new InvalidDataException("Missing projection selection");
        key=Convert.FromHexString(selection.KeyHex);
        if(key.Length!=32||selection.SourcePins.Length is <1 or >16||!Path.IsPathFullyQualified(selection.EvidencePath)||
            !long.TryParse(selection.WriterBirthUtcTicks,out _)||selection.Actors.Keys.Order().SequenceEqual(new[]{"Q","W"})==false)
            throw new InvalidDataException("Closed owner projection selection required");
        Guard();
        } catch {selectionLock.Dispose();CryptographicOperations.ZeroMemory(key);throw;}
    }
    private void Guard() {
        using var writer=Process.GetProcessById(selection.WriterPid);
        if(writer.HasExited||writer.StartTime.ToUniversalTime().Ticks.ToString()!=selection.WriterBirthUtcTicks)throw new InvalidDataException("Selected projection writer ended or changed");
        foreach(var pin in selection.SourcePins){if(!Path.IsPathFullyQualified(pin.Path)||new FileInfo(pin.Path).Length>1048576)throw new InvalidDataException("Bounded writer source pin required");using var file=File.OpenRead(pin.Path);if(Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant()!=pin.Sha256)throw new InvalidDataException("Selected writer source changed");}
    }
    public HubEvidence Read() {
        Guard();using var file=File.Open(selection.EvidencePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        if(file.Length>65536)throw new InvalidDataException("Owner projection exceeds bound");
        var envelope=JsonSerializer.Deserialize<ProjectionEnvelope>(file,HubControl.Json)??throw new InvalidDataException("Missing owner projection");
        var payload=Encoding.UTF8.GetBytes(envelope.Payload);if(payload.Length>32768)throw new InvalidDataException("Owner payload exceeds bound");
        var mac=Convert.FromHexString(envelope.Mac);
        if(!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key,payload),mac))throw new InvalidDataException("Owner projection authentication failed");
        var value=JsonSerializer.Deserialize<OwnerProjection>(payload,HubControl.Json)??throw new InvalidDataException("Empty owner projection");
        var now=DateTimeOffset.UtcNow;
        if(value.Schema!="questms.hub.owner-projection.v1"||value.Generation!=selection.Generation||value.Session!=selection.Session||
            value.CreatedUtc>now||(now-value.CreatedUtc).TotalSeconds>10||value.Actors.Count!=2||value.Actors.Any(pair=>!selection.Actors.TryGetValue(pair.Key,out var actor)||actor!=pair.Value))
            throw new InvalidDataException("Owner projection identity or freshness changed");
        Guard();return new HubEvidence("questms.hub.observations.v1","Selected validated owner",value.Generation,value.Observations);
    }
    public void Dispose(){selectionLock.Dispose();CryptographicOperations.ZeroMemory(key);}
    public static void SelfTest() {
        var directory=Path.Combine(Path.GetTempPath(),"questms-projection-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try {
            var input=Path.Combine(directory,"selection.json");var output=Path.Combine(directory,"projection.json");var source=Path.Combine(directory,"writer.py");File.WriteAllText(source,"modeled selected writer");
            using var process=Process.GetCurrentProcess();var secret=RandomNumberGenerator.GetBytes(32);
            var actors=new Dictionary<string,ProjectionActor>{{"Q",new("app-process.19.Q","9223372036854775800")},{"W",new("app-process.19.W","9223372036854775799")}};
            var selected=new ProjectionSelection(output,Convert.ToHexString(secret),"app-process.19","session.model",process.Id,process.StartTime.ToUniversalTime().Ticks.ToString(),[new(source,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant())],actors);
            File.WriteAllText(input,JsonSerializer.Serialize(selected,HubControl.Json));
            void Write(DateTimeOffset created,Dictionary<string,ProjectionActor> identities,bool badMac=false){var body=JsonSerializer.Serialize(new OwnerProjection("questms.hub.owner-projection.v1",selected.Generation,selected.Session,created,identities,[]),HubControl.Json);var mac=HMACSHA256.HashData(secret,Encoding.UTF8.GetBytes(body));if(badMac)mac[0]^=1;File.WriteAllText(output,JsonSerializer.Serialize(new ProjectionEnvelope(body,Convert.ToHexString(mac)),HubControl.Json));}
            var badSelection=Path.Combine(directory,"bad-selection.json");File.WriteAllText(badSelection,new string('x',8193));
            try{new HubProjectionSource(badSelection);throw new Exception("Oversize selection accepted");}catch(InvalidDataException){}
            using(var exclusive=File.Open(badSelection,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}
            File.WriteAllText(badSelection,JsonSerializer.Serialize(selected with {WriterPid=int.MaxValue},HubControl.Json));
            try{new HubProjectionSource(badSelection);throw new Exception("Absent writer accepted");}catch(ArgumentException){}
            using(var exclusive=File.Open(badSelection,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}
            using var reader=new HubProjectionSource(input);Write(DateTimeOffset.UtcNow,actors);reader.Read();
            var hub=new HubControl(null,reader);
            var qualified=JsonSerializer.SerializeToElement(hub.Status(DateTimeOffset.UtcNow,true,["disconnect"]));
            if(!qualified.GetProperty("ownerObservationAuthenticated").GetBoolean()||qualified.GetProperty("executionVerified").GetBoolean())throw new Exception("Projection authentication and execution readiness conflated");
            var recorded=Path.Combine(directory,"recorded.json");File.WriteAllText(recorded,JsonSerializer.Serialize(new HubEvidence("questms.hub.observations.v1","Selected validated owner",selected.Generation,[]),HubControl.Json));
            if(JsonSerializer.SerializeToElement(new HubControl(recorded).Status(DateTimeOffset.UtcNow)).GetProperty("ownerObservationAuthenticated").GetBoolean())throw new Exception("Copied owner label authenticated a static record");
            void Denied(Action change){change();try{reader.Read();throw new Exception("Invalid owner projection accepted");}catch(InvalidDataException){}}
            Denied(()=>Write(DateTimeOffset.UtcNow,actors,true));Denied(()=>Write(DateTimeOffset.UtcNow.AddSeconds(-11),actors));
            Denied(()=>Write(DateTimeOffset.UtcNow,new Dictionary<string,ProjectionActor>(actors){["Q"]=new("app-process.19.Q","9223372036854775801")}));
            Denied(()=>File.WriteAllText(source,"changed source"));
            if(JsonSerializer.SerializeToElement(hub.Status(DateTimeOffset.UtcNow)).GetProperty("ownerObservationAuthenticated").GetBoolean())throw new Exception("Source drift retained authenticated provenance");
            Console.WriteLine("PASS modeled owner projection MAC, opaque native actor identity, freshness and source change guards; no device observations qualified");
        } finally{Directory.Delete(directory,true);}
    }
}
