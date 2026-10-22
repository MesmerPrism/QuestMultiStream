using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace QuestMultiStream.Browser;

// Actual owner/transport algorithms; the single native invocation is modeled.
// No listeners, native children, devices or capture configuration are used.
internal static class CaptureOwnerModeTests {
    static void Check(bool value,string why){if(!value)throw new Exception(why);}
    static async Task Until(Func<bool> p){var end=DateTimeOffset.UtcNow.AddSeconds(5);while(!p()&&DateTimeOffset.UtcNow<end)await Task.Delay(5);Check(p(),"Mode owner fixture did not finish");}
    static readonly OwnedSource Q=new("Q","deviceQ","Q",new("fixture",1,new string('a',64)));
    static string Root(){var p=Path.Combine(Path.GetTempPath(),"qms-capture-mode-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
    static CaptureOwnerConfig Config(string p)=>new("questms.capture.owner.v1","mode-"+Guid.NewGuid().ToString("N"),Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),p);
    static CaptureOwnerAction Start(CaptureOwnerConfig c,string? mode=CaptureOwnerService.SquareLeft)=>new(mode is null?"questms.capture.action.v2":"questms.capture.action.v4",Guid.NewGuid().ToString("D"),"start",Q.SourceId,c.OwnerEpoch,Q.OwnerBindingRef.Sha256,null,mode);
    static CaptureOwnerAction Stop(CaptureOwnerConfig c,CaptureOwnerResult r,bool v4=false)=>new(v4?"questms.capture.action.v4":"questms.capture.action.v2",Guid.NewGuid().ToString("D"),"stop",Q.SourceId,c.OwnerEpoch,Q.OwnerBindingRef.Sha256,new(r.InvocationId,r.Generation));
    static CaptureOwnerResult Get(CaptureOwnerService s,string id)=>(CaptureOwnerResult)s.Original(id).Body;
    sealed class Invocation:ICaptureInvocation {
        public string InvocationId{get;}=Guid.NewGuid().ToString("N");public string Generation{get;}=Guid.NewGuid().ToString("N");
        public bool Started{get;private set;}public bool RetirementProven{get;set;}public bool Stopping;public bool NativeFailure;public bool FailBeforeStart;public bool StagePending;
        public bool RetirementPending=>StagePending||Started&&!RetirementProven;
        public TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CaptureProducerFacts Facts()=>new(Started,Started,Started,Started?3:0,Started?2:0,Started?"10":null,Started?"20":null,null,null);
        public CaptureTerminalFacts? Terminal=>RetirementProven||NativeFailure?new(NativeFailure?1:0,true,true,NativeFailure?1:0,true,true,true,true,"MODELED exact invocation terminal"):null;
        public async Task Run(CaptureFanout f,CancellationToken t){if(FailBeforeStart){StagePending=true;throw new IOException("MODELED stage attempt before child launch");}Started=true;try{await Task.Delay(Timeout.Infinite,t);}catch(OperationCanceledException){}Stopping=true;await Release.Task;if(NativeFailure)throw new IOException("MODELED native1");RetirementProven=true;}
    }
    static void Invalid(Action action){try{action();throw new Exception("Invalid mode action admitted");}catch(InvalidDataException){}}
    public static async Task Run(){await Legacy();await Lifecycle();await GuardAndUnknown();await PrelaunchOwnership();await OriginalCancellationAndRecovery();await Wire();Console.WriteLine("PASS capture mode owner: legacy raw wire/default, explicit raw/square originals/instance/current, closed modes, mode-conflicting UUID, one source until actual retirement, exact Stop/original Stop/recovery, adjacent guard/no-dispatch, native1 unknown, hostile v4 transport; modeled native effects, no devices/listeners");}
    static async Task Legacy(){var p=Root();try{var c=Config(p);var inv=new Invocation();using var s=new CaptureOwnerService(c,[Q],_=>inv,_=>Task.CompletedTask,(_,_)=>{},(_,_,_)=>{},default);
        var a=Start(c,null);Check(!JsonSerializer.Serialize(a,OwnerJson.Options).Contains("\"mode\""),"Legacy action wire gained mode");s.Submit(a);await Until(()=>inv.Started);var r=Get(s,a.RequestId);
        Check(r.Schema=="questms.capture.result.v2"&&r.Mode is null&&!JsonSerializer.Serialize(r,OwnerJson.Options).Contains("\"mode\""),"Legacy result changed");Check(((CaptureCurrent)s.Current("Q").Body).Schema=="questms.capture.current.v2","Legacy current changed");
        Check(s.Submit(Start(c)).Status==409,"Square without both explicit adapter/validation admitted");Invalid(()=>s.Submit(Start(c,"SQUARE-LEFT")));Invalid(()=>s.Submit(a with{Mode=CaptureOwnerService.RawTwoLens}));
        var stop=Stop(c,r);s.Submit(stop);inv.Release.SetResult();await Until(()=>Get(s,stop.RequestId).State=="retired");
    }finally{Directory.Delete(p,true);}}
    static async Task Lifecycle(){var p=Root();try{var c=Config(p);var allocations=new List<(string Mode,Invocation Inv)>();var guards=0;
        using var s=new CaptureOwnerService(c,[Q],_=>throw new Exception("Wrong default factory"),_=>{guards++;return Task.CompletedTask;},(_,_)=>{},(_,_,_)=>{},default,
            modeFactory:(_,m)=>{var i=new Invocation();allocations.Add((m,i));return i;},modeValidation:(_,m)=>m is CaptureOwnerService.RawTwoLens or CaptureOwnerService.SquareLeft);
        var a=Start(c);s.Submit(a);await Until(()=>allocations[0].Inv.Started);var r=Get(s,a.RequestId);Check(r.Schema=="questms.capture.result.v3"&&r.Mode==CaptureOwnerService.SquareLeft&&guards==1,"Square original/guard not bound");
        var current=(CaptureCurrent)s.Current("Q").Body;Check(current.Schema=="questms.capture.current.v3"&&current.Mode==r.Mode&&current.Operation?.Mode==r.Mode,"Current lost mode");
        var instance=OwnerJson.Read<CaptureInstanceRecord>(File.ReadAllBytes(Path.Combine(p,"operations",a.RequestId,"INSTANCE.json")));Check(instance.Schema=="questms.capture.instance.v2"&&instance.Mode==r.Mode,"Instance mode not durable");
        Check(s.Submit(a).Status==200&&allocations.Count==1,"Duplicate original created a mode producer");Check(s.Submit(a with{Mode=CaptureOwnerService.RawTwoLens}).Status==409,"Same ID changed mode");
        Check(s.Submit(Start(c,CaptureOwnerService.RawTwoLens)).Status==409,"Second mode allocated before Stop");Invalid(()=>s.Submit(Stop(c,r,true)with{Mode=CaptureOwnerService.RawTwoLens}));
        var stop=Stop(c,r,true);s.Submit(stop);await Until(()=>allocations[0].Inv.Stopping);Check(Get(s,stop.RequestId).Mode==CaptureOwnerService.SquareLeft,"Stop did not derive original mode");Check(s.Submit(Start(c,CaptureOwnerService.RawTwoLens)).Status==409,"Mode switched before actual EOF/retirement");
        allocations[0].Inv.Release.SetResult();await Until(()=>Get(s,stop.RequestId).State=="retired");Check(Get(s,stop.RequestId).Terminal is {NativeChildExitCode:0,StdoutEof:true,StderrEof:true,NativeStdoutEof:true,NativeStderrEof:true,ResourcesRetired:true},"Mode Stop lost terminal proof");
        var raw=Start(c,CaptureOwnerService.RawTwoLens);s.Submit(raw);await Until(()=>allocations.Count==2&&allocations[1].Inv.Started);Check(allocations[1].Mode==CaptureOwnerService.RawTwoLens,"Explicit raw adapter mode lost");
        Check(s.Submit(Stop(c,r)).Status==409&&!allocations[1].Inv.Stopping,"Old square Stop affected later raw invocation");
        var end=Stop(c,Get(s,raw.RequestId));s.Submit(end);allocations[1].Inv.Release.SetResult();await Until(()=>Get(s,end.RequestId).State=="retired");Check(Get(s,a.RequestId).Mode==CaptureOwnerService.SquareLeft&&Get(s,raw.RequestId).Mode==CaptureOwnerService.RawTwoLens,"Original mode followed current rearm");
    }finally{Directory.Delete(p,true);}}
    static async Task GuardAndUnknown(){var p=Root();try{var c=Config(p);var inv=new Invocation();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var allowed=true;
        using var s=new CaptureOwnerService(c,[Q],_=>inv,async _=>{entered.SetResult();await gate.Task;},(_,_)=>{},(_,_,_)=>{},default,modeFactory:(_,_)=>inv,modeValidation:(_,_)=>allowed);
        var a=Start(c);s.Submit(a);await entered.Task;allowed=false;gate.SetResult();await Until(()=>Get(s,a.RequestId).State=="failed");var r=Get(s,a.RequestId);Check(!inv.Started&&r.Mode==CaptureOwnerService.SquareLeft&&r.NoDispatchProven&&!r.RetirementPending&&r.Terminal is null,"Late mode guard did not produce honest no-dispatch");
    }finally{Directory.Delete(p,true);}
        p=Root();try{var c=Config(p);var inv=new Invocation{NativeFailure=true};using var s=new CaptureOwnerService(c,[Q],_=>inv,_=>Task.CompletedTask,(_,_)=>{},(_,_,_)=>{},default,modeFactory:(_,_)=>inv,modeValidation:(_,_)=>true);
        var a=Start(c);s.Submit(a);await Until(()=>inv.Started);var stop=Stop(c,Get(s,a.RequestId));s.Submit(stop);inv.Release.SetResult();await Until(()=>Get(s,stop.RequestId).State=="unknown");Check(Get(s,stop.RequestId)is{Mode:CaptureOwnerService.SquareLeft,RetirementPending:true,NoDispatchProven:false,ActualEffectsCompleted:false},"Native1 gained mode retirement");Check(s.Submit(Start(c,CaptureOwnerService.RawTwoLens)).Status==409,"Native1 mode replaced");inv.RetirementProven=true; // Modeled fixture teardown only.
    }finally{Directory.Delete(p,true);}}
    static async Task PrelaunchOwnership(){
        foreach(var beforeGuard in new[]{false,true}){var p=Root();try{var c=Config(p);var inv=new Invocation{FailBeforeStart=!beforeGuard,StagePending=beforeGuard};
            using var s=new CaptureOwnerService(c,[Q],_=>inv,_=>beforeGuard?Task.FromException(new InvalidDataException("MODELED guard refused after retained stage")):Task.CompletedTask,(_,_)=>{},(_,_,_)=>{},default,modeFactory:(_,_)=>inv,modeValidation:(_,_)=>true);
            var action=Start(c);s.Submit(action);await Until(()=>Get(s,action.RequestId).State=="unknown");var result=Get(s,action.RequestId);
            Check(!inv.Started&&result is{RetirementPending:true,NoDispatchProven:false,ActualEffectsCompleted:false,Terminal:null},"Prelaunch stage became absence/retirement");
            Check(!File.Exists(Path.Combine(p,"operations",action.RequestId,"NO_DISPATCH.json"))&&s.Submit(Start(c,CaptureOwnerService.RawTwoLens)).Status==409,"Prelaunch stage allowed replacement/no-dispatch proof");
            try{s.Dispose();throw new Exception("Unretired prelaunch stage released writer");}catch(InvalidOperationException){}
            inv.StagePending=false;inv.RetirementProven=true; // Explicit modeled fixture teardown only; no stored proof/adoption.
        }finally{Directory.Delete(p,true);}}
        var path=Root();try{var c=Config(path);var inv=new Invocation{StagePending=true};using var s=new CaptureOwnerService(c,[Q],_=>inv,_=>Task.CompletedTask,(_,_)=>{},(_,_,_)=>{},default,modeFactory:(_,_)=>inv,modeValidation:(_,_)=>true);
            var start=Start(c);s.SubmitOriginalStop(new("questms.capture.action.v3",Guid.NewGuid().ToString("D"),"stop","Q",c.OwnerEpoch,Q.OwnerBindingRef.Sha256,start.RequestId));s.Submit(start);
            Check(Get(s,start.RequestId)is{State:"unknown",NoDispatchProven:false,RetirementPending:true},"Cancelled original fabricated no-dispatch for pending factory allocation");
            Check(!File.Exists(Path.Combine(path,"operations",start.RequestId,"NO_DISPATCH.json")),"Pending cancelled allocation created negative proof");
            try{s.Dispose();throw new Exception("Pending detached allocation released writer");}catch(InvalidOperationException){}
            inv.StagePending=false;inv.RetirementProven=true; // Explicit modeled fixture teardown only.
        }finally{Directory.Delete(path,true);}
    }
    static async Task OriginalCancellationAndRecovery(){var p=Root();try{var c=Config(p);var list=new List<Invocation>();var start=Start(c);var cancel=new CaptureOriginalStopAction("questms.capture.action.v3",Guid.NewGuid().ToString("D"),"stop","Q",c.OwnerEpoch,Q.OwnerBindingRef.Sha256,start.RequestId);
        using(var s=new CaptureOwnerService(c,[Q],_=>throw new Exception("Wrong raw factory"),_=>Task.CompletedTask,(_,_)=>{},(_,_,_)=>{},default,modeFactory:(_,_)=>{var i=new Invocation();list.Add(i);return i;},modeValidation:(_,_)=>true)){
            s.Submit(start);await Until(()=>list[0].Started);s.SubmitOriginalStop(cancel);list[0].Release.SetResult();await Until(()=>((CaptureOriginalStopResult)s.Original(cancel.RequestId).Body).State=="retired");Check(((CaptureOriginalStopResult)s.Original(cancel.RequestId).Body).Target?.Mode==CaptureOwnerService.SquareLeft,"Original-target Stop lost square mode");
            var late=Start(c);var before=cancel with{RequestId=Guid.NewGuid().ToString("D"),OriginalStartRequestId=late.RequestId};s.SubmitOriginalStop(before);s.Submit(late);Check(Get(s,late.RequestId)is{Mode:CaptureOwnerService.SquareLeft,NoDispatchProven:true,Terminal:null}&&!list[1].Started,"Prearrival v3 cancellation lost mode or dispatched");
        }
        var calls=0;using(var recovered=new CaptureOwnerService(c,[Q],_=>{calls++;return new Invocation();},_=>Task.CompletedTask,(_,_)=>{},(_,_,_)=>{},default)){
            Check(Get(recovered,start.RequestId).Mode==CaptureOwnerService.SquareLeft&&((CaptureOriginalStopResult)recovered.Original(cancel.RequestId).Body).Target?.Mode==CaptureOwnerService.SquareLeft&&calls==0,"Square recovery reallocated/lost original mode");
        }
        var path=Path.Combine(p,"operations",start.RequestId,"INSTANCE.json");var record=OwnerJson.Read<CaptureInstanceRecord>(File.ReadAllBytes(path));File.WriteAllBytes(path,JsonSerializer.SerializeToUtf8Bytes(record with{Mode=CaptureOwnerService.RawTwoLens},OwnerJson.Options));
        Invalid(()=>{using var bad=new CaptureOwnerService(c,[Q],_=>throw new Exception("Recovery factory invoked"),_=>Task.CompletedTask,(_,_)=>{},(_,_,_)=>{},default);});
    }finally{Directory.Delete(p,true);}}
    static async Task Wire(){var p=Root();try{var c=Config(p);var inv=new Invocation();using var s=new CaptureOwnerService(c,[Q],_=>inv,_=>Task.CompletedTask,(_,_)=>{},(_,_,_)=>{},default,modeFactory:(_,_)=>inv,modeValidation:(_,_)=>true);var wire=new CaptureOwnerTransport(c,s,"127.0.0.1:41234");
        CaptureWireReply Post(byte[] body){var nonce=CaptureOwnerTransport.NewNonce(DateTimeOffset.UtcNow);return wire.Exchange("POST","/api/owner/capture-actions",[new("Host","127.0.0.1:41234"),new("Content-Type","application/json"),new("X-Quest-Owner-Epoch",c.OwnerEpoch),new("X-Quest-Nonce",nonce),new("X-Quest-Mac",HubOwnerBridge.Mac(c.Key,CaptureOwnerTransport.RequestDomain,"POST","/api/owner/capture-actions",c.OwnerEpoch,nonce,body))],body,IPAddress.Loopback);}
        var raw=Start(c,null);var bad=JsonSerializer.SerializeToUtf8Bytes(raw with{Mode=CaptureOwnerService.SquareLeft},OwnerJson.Options);Check(Post(bad).Status==400&&!inv.Started,"v2 mode extension admitted");Check(Post(JsonSerializer.SerializeToUtf8Bytes(Start(c,"foreign"),OwnerJson.Options)).Status==400,"Unknown wire mode admitted");
        var action=Start(c);Check(Post(JsonSerializer.SerializeToUtf8Bytes(action,OwnerJson.Options)).Status==202,"Authenticated square action refused");await Until(()=>inv.Started);Check(Post(JsonSerializer.SerializeToUtf8Bytes(action with{Mode=CaptureOwnerService.RawTwoLens},OwnerJson.Options)).Status==409,"Wire idempotency ignored mode");var stop=Stop(c,Get(s,action.RequestId));s.Submit(stop);inv.Release.SetResult();await Until(()=>Get(s,stop.RequestId).State=="retired");
    }finally{Directory.Delete(p,true);}}
}
