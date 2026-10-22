using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuestMultiStream.Browser;

// Pure file-backed identity boundary tests. These deliberately use a disabled,
// unvalidated HostSelection and non-executable temp tools: no ADB/process path
// is entered by RequireSameTarget. Semantic mutants receive fresh valid file
// pins, so their refusal tests the join rather than an incidental hash failure.
internal static class NativeCaptureBindingTests
{
    static OwnedPin Pin(string path) => new(path, new FileInfo(path).Length,
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());

    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"qms-native-binding-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var adb=Path.Combine(root,"inert-adb.bin");
            var otherAdb=Path.Combine(root,"other-inert-adb.bin");
            var jar=Path.Combine(root,"inert-helper.jar");
            File.WriteAllText(adb,"NOT AN EXECUTABLE: file-only ADB identity fixture",Encoding.UTF8);
            File.Copy(adb,otherAdb);
            File.WriteAllText(jar,"NOT A HELPER: file-only source pin",Encoding.UTF8);
            var adbPin=Pin(adb);var jarPin=Pin(jar);
            var source=new SourceSelection("W","MODELED W","192.0.2.20:5555","MODELEDQUESTW",
                "bdf498c1-54a8-4c08-935e-a60363265da7","57710326-75c6-4052-84be-76516d9b490d",41235);
            var transport=new HostSelection(false,null,adb,adbPin.Sha256,jar,jarPin.Sha256,
                jar,jarPin.Sha256,jar,jarPin.Sha256,[source],-77,-1,root);
            var native=new NativeCaptureSelection("questms.native-square.selection.v1",transport,
                jarPin,"io.github.mesmerprism.rustyquest.nativecapture.NativeBrowserCapture",new string('d',64));
            try { native.Validate(); throw new Exception("Implicit global native authority accepted"); }
            catch (InvalidDataException error) when (error.Message.Contains("global native capture authority")) { }
            (native with { GlobalNativeCaptureAuthorityAcknowledged=true }).RequireGlobalAuthority();
            var binding=new NativeRawOwnerBinding("rusty.hostess.display_stream.owner_binding.v1",
                adbPin,jarPin,source.Serial,source.Canonical,source.Boot,41235,0,3664,1920,72,16000000);
            var files=0;var accepted=0;var rejected=0;
            OwnedDisplaySelection Selection(byte[] body,string id="W")
            {
                var path=Path.Combine(root,$"owner-binding-{files++}.json");
                using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
                {file.Write(body);file.Flush(true);}
                return new("questms.display.selection.v1",jarPin,jarPin,root,
                    [new(id,"MODELED-deviceW","MODELED W",Pin(path))]);
            }
            OwnedDisplaySelection With(NativeRawOwnerBinding value) =>
                Selection(JsonSerializer.SerializeToUtf8Bytes(value,OwnerJson.Options));
            void Accept(OwnedDisplaySelection owned,HostSelection? raw=null)
            {
                // Exercise the real held file concurrently with the method's
                // own original Hold/ReadExactly/closed parser/target checks.
                using var held=OwnedDisplaySelection.Hold(owned.Sources[0].OwnerBindingRef);
                native.RequireSameTarget(raw,owned);accepted++;
            }
            void Reject(string label,Action check)
            {
                try{check();}
                catch(Exception error)when(error is InvalidDataException or JsonException)
                {rejected++;return;}
                throw new Exception("Native capture binding accepted "+label);
            }

            var exact=With(binding);
            Accept(exact,transport);
            Accept(With(binding with{Adb=adbPin with{Path=Path.Combine(root,".","inert-adb.bin")}}));
            Reject("same SourceId / wrong canonical",()=>native.RequireSameTarget(null,With(binding with{Canonical="ANOTHERQUEST"})));
            Reject("same SourceId / wrong boot",()=>native.RequireSameTarget(null,With(binding with{BootId=Guid.NewGuid().ToString("D")})));
            Reject("same SourceId / wrong transport",()=>native.RequireSameTarget(null,With(binding with{Transport="192.0.2.21:5555"})));
            Reject("same SourceId / wrong ADB hash",()=>native.RequireSameTarget(null,With(binding with{Adb=adbPin with{Sha256=new string('0',64)}})));
            Reject("same SourceId / wrong ADB bytes",()=>native.RequireSameTarget(null,With(binding with{Adb=adbPin with{Bytes=adbPin.Bytes+1}})));
            Reject("same bytes / different ADB path",()=>native.RequireSameTarget(null,With(binding with{Adb=Pin(otherAdb)})));
            Reject("wrong binding schema",()=>native.RequireSameTarget(null,With(binding with{Schema="foreign.owner_binding.v1"})));
            Reject("missing selected SourceId",()=>native.RequireSameTarget(null,
                Selection(JsonSerializer.SerializeToUtf8Bytes(binding,OwnerJson.Options),"Q")));

            var json=JsonSerializer.Serialize(binding,OwnerJson.Options);
            Reject("duplicate canonical member",()=>native.RequireSameTarget(null,
                Selection(Encoding.UTF8.GetBytes(json.Insert(1,"\"canonical\":\"MODELEDQUESTW\",")))));
            Reject("case-aliased member",()=>native.RequireSameTarget(null,
                Selection(Encoding.UTF8.GetBytes(json.Replace("\"canonical\":","\"Canonical\":")))));
            Reject("case-aliased duplicate member",()=>native.RequireSameTarget(null,
                Selection(Encoding.UTF8.GetBytes(json.Insert(1,"\"Canonical\":\"MODELEDQUESTW\",")))));
            Reject("case-aliased nested ADB member",()=>native.RequireSameTarget(null,
                Selection(Encoding.UTF8.GetBytes(json.Replace("\"sha256\":","\"Sha256\":")))));

            foreach(var other in new[]{source with{Id="Q"},source with{Serial="192.0.2.21:5555"},
                source with{Canonical="ANOTHERQUEST"},source with{Boot=Guid.NewGuid().ToString("D")},
                source with{LeaseId=Guid.NewGuid().ToString("D")}})
                Reject("existing raw selection target mismatch",()=>native.RequireSameTarget(transport with{Sources=[other]},exact));

            var badPin=exact.Sources[0].OwnerBindingRef with{Sha256=new string('0',64)};
            Reject("owner file pin hash mismatch",()=>native.RequireSameTarget(null,
                exact with{Sources=[exact.Sources[0] with{OwnerBindingRef=badPin}]}));
            Reject("owner file pin byte mismatch",()=>native.RequireSameTarget(null,
                exact with{Sources=[exact.Sources[0] with{OwnerBindingRef=exact.Sources[0].OwnerBindingRef with{Bytes=badPin.Bytes+1}}]}));

            Console.WriteLine($"PASS native capture target binding: {accepted} positive held-file joins, {rejected} physical/tool/schema/member/pin/raw-selection refusals; explicit global authority refusal/acknowledgment; pure temp files, no live tool checks/ADB/devices");
        }
        finally
        {
            // Successful deletion also catches a leaked original binding Hold.
            Directory.Delete(root,true);
        }
    }
}
