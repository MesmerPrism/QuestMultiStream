namespace QuestMultiStream.Browser;

// Both typed CLI and authenticated UI routes use the same bounded application handler.
public sealed class HubActionHandler(HubControl hub, HubOwnerBridge? bridge)
{
    public async Task<object> Handle(HubAction request) {
        // A human disconnect intent may latch desired-stopped while an original
        // is unresolved. Readiness is not cleanup proof or intent admission;
        // the selected service must apply its own serialized fence/guards.
        if(request.Execute){if(bridge?.LiveSupported==true)await bridge.Current();return await (bridge??throw new InvalidDataException("App-owner executor unavailable; device effects denied")).Execute(request);}
        if(bridge?.LiveSupported==true){var current=await bridge.Current();if(request.Generation!=current.Selection.Generation||request.Direction!="both"||!bridge.Actions.Contains(request.Action)||!Guid.TryParseExact(request.RequestId,"D",out _))throw new InvalidDataException("Current pair selection required");return new HubActionResult("prepared",request.Action,request.Direction,request.Generation,request.RequestId,false,"Intent only; live owner checks admission when dispatched");}
        return hub.Prepare(request);
    }
}
