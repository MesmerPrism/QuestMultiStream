namespace QuestMultiStream.Browser;

// Shared by the production host and isolated HTTP controls. No capture effects.
internal static class OwnerV2Routes
{
    internal static void Map(WebApplication app,HubOwnerBridge? bridge,Func<HttpContext,bool> authorized,Func<HttpContext,string> origin,SemaphoreSlim selectionLane,Func<bool> displaysIdle)
    {
        async Task<IResult> Read(Func<HubOwnerBridge,Task<object>> operation,HttpContext context){
            if(!authorized(context))return Results.Unauthorized();
            try{return Results.Json(await operation(bridge??throw new InvalidDataException("Owner unavailable")),OwnerJson.Options);}
            catch{return Results.Json(new{error="Owner readback unavailable; retained outcomes remain unknown"},statusCode:503);}
        }
        app.MapGet("/api/hub/v2/capabilities",(Func<HttpContext,Task<IResult>>)((HttpContext c)=>Read(async b=>await b.CapabilitiesV2(),c)));
        app.MapGet("/api/hub/v2/status",(Func<HttpContext,Task<IResult>>)((HttpContext c)=>Read(async b=>await b.CurrentV2(),c)));
        app.MapGet("/api/hub/v2/devices",(Func<HttpContext,Task<IResult>>)((HttpContext c)=>Read(async b=>await b.DevicesV2(),c)));
        app.MapGet("/api/hub/v2/runs",(Func<HttpContext,Task<IResult>>)((HttpContext c)=>Read(async b=>await b.RunsV2(),c)));
        app.MapGet("/api/hub/v2/actions/{requestId}",(HttpContext c,string requestId)=>Read(async b=>await b.StatusV2(requestId),c));
        async Task<byte[]?> Body(HttpContext c){
            if(c.Request.ContentLength is null or >4096 or <2)return null;
            var buffer=new byte[4097];int count=0;
            while(count<buffer.Length){var n=await c.Request.Body.ReadAsync(buffer.AsMemory(count),c.RequestAborted);if(n==0)break;count+=n;}
            return count>4096?null:buffer.AsSpan(0,count).ToArray();
        }
        app.MapPost("/api/hub/v2/actions",async(HttpContext c)=>{
            if(!authorized(c)||c.Request.Headers.Origin!=origin(c))return Results.Unauthorized();
            try{var body=await Body(c);if(body is null)return Results.BadRequest();var request=OwnerJson.Read<OwnerActionV2>(body);if(bridge?.ProtocolVersion!=2)return Results.StatusCode(503);return Results.Json(await bridge.ExecuteV2(request),OwnerJson.Options);}
            catch(Exception e)when(e is InvalidDataException or System.Text.Json.JsonException){return Results.BadRequest(new{error=e.Message});}
            catch{return Results.Json(new{error="Outcome unresolved; retain original request ID and read it without replay"},statusCode:502);}
        });
        app.MapPost("/api/hub/v2/selection",async(HttpContext c)=>{
            if(!authorized(c)||c.Request.Headers.Origin!=origin(c))return Results.Unauthorized();
            await selectionLane.WaitAsync(c.RequestAborted);try{
                if(!displaysIdle())return Results.Conflict(new{error="Display owners remain active or unresolved"});
                var body=await Body(c);if(body is null)return Results.BadRequest();
                var request=OwnerJson.Read<OwnerSelectionV2>(body);if(bridge?.ProtocolVersion!=2)return Results.StatusCode(503);
                return Results.Json(await bridge.SelectV2(request),OwnerJson.Options);
            }catch(Exception e)when(e is InvalidDataException or System.Text.Json.JsonException){return Results.BadRequest(new{error=e.Message});}
            catch{return Results.Json(new{error="Selection unresolved; read current status before another intent"},statusCode:502);}
            finally{selectionLane.Release();}
        });
    }
}
