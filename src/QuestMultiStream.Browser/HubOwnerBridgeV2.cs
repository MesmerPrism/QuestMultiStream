using System.Text.Json;

namespace QuestMultiStream.Browser;

public sealed partial class HubOwnerBridge
{
    // Local no-dispatch results share this existing service-lifetime cache; no durable denial journal.
    readonly Dictionary<string,(OwnerActionV2 Input,Task<OwnerResultV2> Result,bool NotDispatched)> v2Requests=[];
    long v2Revision=-1;
    string V2Epoch(){if(ProtocolVersion!=2||!LiveSupported)throw new InvalidDataException("Authenticated v2 owner not selected");return selection.OwnerEpoch!;}
    public async Task<OwnerCapabilitiesV2> CapabilitiesV2(bool blendAmountRead=false){var epoch=V2Epoch();var result=await Exchange<OwnerCapabilitiesV2>(HttpMethod.Get,"/v2/capabilities",[],blendAmountRead);result.Validate(epoch);return blendAmountRead?result:result with {Features=null};}
    public async Task<OwnerStatusV2> CurrentV2(bool blendAmountRead=false){var epoch=V2Epoch();var result=await Exchange<OwnerStatusV2>(HttpMethod.Get,"/v2/status",[],blendAmountRead);result.Validate(epoch);lock(sync){if(result.Revision<v2Revision)throw new InvalidDataException("Owner v2 revision moved backwards");v2Revision=result.Revision;}return blendAmountRead?result:result with {Peers=result.Peers.Select(p=>p with {BlendAmount=null}).ToArray()};}
    public async Task<OwnerDevicesV2> DevicesV2(){var epoch=V2Epoch();var result=await Exchange<OwnerDevicesV2>(HttpMethod.Get,"/v2/devices",[]);result.Validate(epoch);return result;}
    public async Task<OwnerRunsV2> RunsV2(){var epoch=V2Epoch();var result=await Exchange<OwnerRunsV2>(HttpMethod.Get,"/v2/runs",[]);result.Validate(epoch);return result;}
    public async Task<OwnerStatusV2> SelectV2(OwnerSelectionV2 request){var epoch=V2Epoch();request.Validate(epoch);var result=await Exchange<OwnerStatusV2>(HttpMethod.Post,"/v2/selection",JsonSerializer.SerializeToUtf8Bytes(request,OwnerJson.Options));result.Validate(epoch);V2.Require(result.Members.OrderBy(m=>m.Role).SequenceEqual(request.Members.OrderBy(m=>m.Role)),"Selected v2 pair differs");return result with {Peers=result.Peers.Select(p=>p with {BlendAmount=null}).ToArray()};}
    Task<OwnerResultV2>? ExistingV2(OwnerActionV2 request){
        if(!v2Requests.TryGetValue(request.RequestId,out var prior))return null;
        V2.Require(JsonSerializer.Serialize(prior.Input,OwnerJson.Options)==JsonSerializer.Serialize(request,OwnerJson.Options),"Conflicting retained request");return prior.Result;
    }
    public async Task<OwnerResultV2> ExecuteV2(OwnerActionV2 request){
        request.Validate(V2Epoch());Task<OwnerResultV2>? retained;lock(sync)retained=ExistingV2(request);if(retained is not null)return await retained;
        bool supported=true;
        if(request.BlendAmountBits is not null){try{supported=(await CapabilitiesV2(true)).Features?.Contains(V2.BlendFeature)==true;}catch{supported=false;}}
        lock(sync){
            retained=ExistingV2(request);
            if(retained is null){
                V2.Require(v2Requests.Count<256,"Owner v2 request capacity reached");
                retained=supported?SendV2(request):Task.FromResult(new OwnerResultV2("questms.owner.result.v2",request.RequestId,request.OwnerEpoch,request.SelectionId,request.Action,"failed",false,[],"blend_amount_unsupported_not_dispatched"));
                v2Requests.Add(request.RequestId,(request,retained,!supported));
            }
        }
        return await retained;
    }
    async Task<OwnerResultV2> SendV2(OwnerActionV2 request){
        var result=ValidateV2(await Exchange<OwnerResultV2>(HttpMethod.Post,"/v2/actions",JsonSerializer.SerializeToUtf8Bytes(request,OwnerJson.Options)),request.RequestId,request);
        return request.BlendAmountBits is not null?result:result with {Peers=result.Peers.Select(p=>p with {BlendAmount=null}).ToArray()};
    }
    public async Task<OwnerResultV2> StatusV2(string requestId,bool blendAmountRead=false){
        V2.Id(requestId,true);OwnerActionV2? input;Task<OwnerResultV2>? local=null;
        lock(sync){input=v2Requests.TryGetValue(requestId,out var prior)?prior.Input:null;if(prior.NotDispatched)local=prior.Result;}
        if(local is not null)return await local;
        var result=ValidateV2(await Exchange<OwnerResultV2>(HttpMethod.Get,"/v2/actions/"+requestId,[],blendAmountRead),requestId,input,blendAmountRead);
        return blendAmountRead&&(input is null||input.BlendAmountBits is not null)?result:result with {Peers=result.Peers.Select(p=>p with {BlendAmount=null}).ToArray()};
    }
    OwnerResultV2 ValidateV2(OwnerResultV2 result,string requestId,OwnerActionV2? input,bool requireNumeric=true){
        result.Validate(V2Epoch(),requestId);
        if(input is not null){
            V2.Require(result.SelectionId==input.SelectionId&&result.Action==input.Action,"Retained result/action join differs");
            if(requireNumeric&&input.BlendAmountBits is {} bits&&result.State=="effective"&&result.Peers.Any(p=>p.BlendAmount?.AmountBits!=bits))throw new HttpRequestException("Numeric completion awaits both qualified Blend readbacks; retain Original");
        }
        return result;
    }
}
