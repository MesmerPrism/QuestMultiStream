using System.Text.Json;

namespace QuestMultiStream.Browser;

public sealed partial class HubOwnerBridge
{
    readonly Dictionary<string,(OwnerActionV2 Input,Task<OwnerResultV2> Result)> v2Requests=[];
    long v2Revision=-1;
    string V2Epoch(){if(ProtocolVersion!=2||!LiveSupported)throw new InvalidDataException("Authenticated v2 owner not selected");return selection.OwnerEpoch!;}
    public async Task<OwnerCapabilitiesV2> CapabilitiesV2(){var epoch=V2Epoch();var result=await Exchange<OwnerCapabilitiesV2>(HttpMethod.Get,"/v2/capabilities",[]);result.Validate(epoch);return result;}
    public async Task<OwnerStatusV2> CurrentV2(){var epoch=V2Epoch();var result=await Exchange<OwnerStatusV2>(HttpMethod.Get,"/v2/status",[]);result.Validate(epoch);lock(sync){if(result.Revision<v2Revision)throw new InvalidDataException("Owner v2 revision moved backwards");v2Revision=result.Revision;}return result;}
    public async Task<OwnerDevicesV2> DevicesV2(){var epoch=V2Epoch();var result=await Exchange<OwnerDevicesV2>(HttpMethod.Get,"/v2/devices",[]);result.Validate(epoch);return result;}
    public async Task<OwnerRunsV2> RunsV2(){var epoch=V2Epoch();var result=await Exchange<OwnerRunsV2>(HttpMethod.Get,"/v2/runs",[]);result.Validate(epoch);return result;}
    public async Task<OwnerStatusV2> SelectV2(OwnerSelectionV2 request){var epoch=V2Epoch();request.Validate(epoch);var result=await Exchange<OwnerStatusV2>(HttpMethod.Post,"/v2/selection",JsonSerializer.SerializeToUtf8Bytes(request,OwnerJson.Options));result.Validate(epoch);V2.Require(result.Members.OrderBy(m=>m.Role).SequenceEqual(request.Members.OrderBy(m=>m.Role)),"Selected v2 pair differs");return result;}
    public Task<OwnerResultV2> ExecuteV2(OwnerActionV2 request){
        request.Validate(V2Epoch());lock(sync){
            if(v2Requests.TryGetValue(request.RequestId,out var prior)){V2.Require(JsonSerializer.Serialize(prior.Input,OwnerJson.Options)==JsonSerializer.Serialize(request,OwnerJson.Options),"Conflicting retained request");return prior.Result;}
            V2.Require(v2Requests.Count<256,"Owner v2 request capacity reached");var result=SendV2(request);v2Requests.Add(request.RequestId,(request,result));return result;
        }
    }
    async Task<OwnerResultV2> SendV2(OwnerActionV2 request)=>ValidateV2(await Exchange<OwnerResultV2>(HttpMethod.Post,"/v2/actions",JsonSerializer.SerializeToUtf8Bytes(request,OwnerJson.Options)),request.RequestId,request);
    public async Task<OwnerResultV2> StatusV2(string requestId){V2.Id(requestId,true);OwnerActionV2? input;lock(sync)input=v2Requests.TryGetValue(requestId,out var prior)?prior.Input:null;return ValidateV2(await Exchange<OwnerResultV2>(HttpMethod.Get,"/v2/actions/"+requestId,[]),requestId,input);}
    OwnerResultV2 ValidateV2(OwnerResultV2 result,string requestId,OwnerActionV2? input){result.Validate(V2Epoch(),requestId);if(input is not null)V2.Require(result.SelectionId==input.SelectionId&&result.Action==input.Action,"Retained result/action join differs");return result;}
}
