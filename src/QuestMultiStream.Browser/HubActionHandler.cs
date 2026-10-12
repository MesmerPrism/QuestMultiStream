namespace QuestMultiStream.Browser;

// Both typed CLI and authenticated UI routes use the same bounded application handler.
public sealed class HubActionHandler(HubControl hub, HubOwnerBridge? bridge)
{
    public async Task<object> Handle(HubAction request) => request.Execute
        ? await (bridge ?? throw new InvalidDataException("App-owner executor unavailable; device effects denied")).Execute(request)
        : hub.Prepare(request);
}
