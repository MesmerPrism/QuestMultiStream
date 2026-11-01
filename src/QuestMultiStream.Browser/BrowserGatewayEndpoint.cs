using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Features;

namespace QuestMultiStream.Browser;

internal sealed record BrowserGatewaySelection(string ListenAddress,int Port,string PublicAppUrl)
{
    internal string Origin=>new Uri(PublicAppUrl).GetLeftPart(UriPartial.Authority);
    internal string GatewayOrigin=>$"http://{ListenAddress}:{Port}";
    internal void Validate()
    {
        if(!IPAddress.TryParse(ListenAddress,out var address)||address.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork||address.ToString()!=ListenAddress)throw new InvalidDataException("Canonical private or loopback IPv4 required");
        var b=address.GetAddressBytes();
        if(!(IPAddress.IsLoopback(address)||b[0]==10||b[0]==172&&b[1] is >=16 and <=31||b[0]==192&&b[1]==168)||Port is <1024 or >65535)throw new InvalidDataException("Private or loopback listener and operator port required");
        if(PublicAppUrl is null||PublicAppUrl.Length>2048||!Uri.TryCreate(PublicAppUrl,UriKind.Absolute,out var app)||app.Scheme!="https"||app.UserInfo.Length!=0||app.Query.Length!=0||app.Fragment.Length!=0||app.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6)||app.AbsoluteUri!=PublicAppUrl)throw new InvalidDataException("Exact public HTTPS application URL without credentials, query or fragment required");
    }
}

internal sealed class BrowserGatewayEndpoint(BrowserGatewaySelection selection)
{
    internal BrowserGatewaySelection Selection=>selection;
    internal static BrowserGatewayEndpoint? Load(string? path)
    {
        if(path is null)return null;
        if(!Path.IsPathFullyQualified(path))throw new InvalidDataException("Absolute private gateway selection required");
        using var file=File.OpenRead(path);if(file.Length is <2 or >4096)throw new InvalidDataException("Bounded gateway selection required");
        var bytes=new byte[(int)file.Length];file.ReadExactly(bytes);var selected=OwnerJson.Read<BrowserGatewaySelection>(bytes);selected.Validate();return new(selected);
    }
    internal void Configure(WebApplicationBuilder builder,int loopbackPort)
    {
        if(selection.Port==loopbackPort)throw new InvalidDataException("Distinct explicit gateway port required");
        builder.Services.Configure<ForwardedHeadersOptions>(o=>o.ForwardedHeaders=Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.None);
        builder.WebHost.ConfigureKestrel(o=>{
            o.Configure(new ConfigurationBuilder().Build(),reloadOnChange:false);
            o.Listen(IPAddress.Loopback,loopbackPort);
            o.Listen(IPAddress.Parse(selection.ListenAddress),selection.Port);
        });
    }
    internal void Guard(WebApplication app,string localOrigin,string token)=>app.Use((context,next)=>Process(context,next,localOrigin,token));
    internal bool IsSelected(HttpContext c)=>c.Features.Get<ITlsConnectionFeature>() is null&&c.Request.Scheme=="http"&&c.Connection.LocalPort==selection.Port&&c.Connection.LocalIpAddress?.Equals(IPAddress.Parse(selection.ListenAddress))==true;
    internal bool SameOrigin(HttpContext c)=>c.Request.Headers.Origin.Count==1&&c.Request.Headers.Origin[0]==selection.Origin;
    internal string Origin(HttpContext c,string localOrigin)=>IsSelected(c)?selection.Origin:localOrigin;
    internal bool Authorized(HttpContext c,string token)
    {
        if(!SameOrigin(c))return false;
        string? supplied;
        if(c.WebSockets.IsWebSocketRequest&&SocketPath(c.Request.Path.Value??"")){
            var values=c.Request.Query["token"];
            if(c.Request.Headers.ContainsKey("Authorization")||values.Count!=1)return false;
            supplied=values[0];
        }else{
            if(c.Request.Query.ContainsKey("token"))return false;
            var header=c.Request.Headers.Authorization;
            if(header.Count!=1||header[0] is not {} value||!value.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase))return false;
            supplied=value[7..];
        }
        return supplied is not null&&CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied),Encoding.UTF8.GetBytes(token));
    }
    internal async Task Process(HttpContext c,RequestDelegate next,string localOrigin,string token)
    {
        if(!IsSelected(c)){
            // Never infer authority from forwarded headers or merely from Host.
            if(c.Features.Get<ITlsConnectionFeature>() is null&&c.Request.Scheme=="http"&&c.Connection.LocalIpAddress?.Equals(IPAddress.Loopback)==true&&c.Connection.LocalPort==new Uri(localOrigin).Port&&c.Request.Host.Value==new Uri(localOrigin).Authority){await next(c);return;}
            Deny(c,403);return;
        }
        NoStore(c);
        var path=c.Request.Path.Value??"";
        // Encoded/ambiguous paths are unnecessary on this closed API transport.
        // Reject before routing and before preflight, including escaped owner aliases.
        if(c.Request.Host.Value!=new Uri(selection.GatewayOrigin).Authority||path.Contains('%')||path.Contains('\\')||path.Equals("/api/owner",StringComparison.OrdinalIgnoreCase)||path.StartsWith("/api/owner/",StringComparison.OrdinalIgnoreCase)){Deny(c,403);return;}
        if(!SameOrigin(c)){Deny(c,403);return;}
        if(c.Request.Method=="OPTIONS"){
            if(!Preflight(c,path)){Deny(c,403);return;}
            Cors(c);c.Response.Headers["Access-Control-Allow-Methods"]=c.Request.Headers["Access-Control-Request-Method"];
            if(c.Request.Headers.ContainsKey("Access-Control-Request-Headers"))c.Response.Headers["Access-Control-Allow-Headers"]="Content-Type, Accept, Authorization";
            c.Response.StatusCode=204;return;
        }
        Cors(c);
        if(!AllowedRoute(c.Request.Method,path,c.WebSockets.IsWebSocketRequest)){Deny(c,403);return;}
        if(!Authorized(c,token)){Deny(c,401);return;}
        await next(c);
    }
    static bool Segment(string value)=>value.Length is >0 and <=128&&value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_' or '.');
    static bool Suffix(string path,string prefix)=>path.StartsWith(prefix,StringComparison.Ordinal)&&Segment(path[prefix.Length..]);
    static bool SocketPath(string path)=>Suffix(path,"/observe/")||Suffix(path,"/stream/");
    static bool AllowedRoute(string method,string path,bool socket=false)
    {
        if(socket)return method=="GET"&&SocketPath(path);
        if(method=="GET")return path is "/api/sources" or "/api/fixture" or "/fixture" or "/api/captures" or "/api/hub" or "/api/hub/capabilities" or "/api/hub/current" or "/api/hub/devices" or "/api/hub/session" or "/api/hub/v2/capabilities" or "/api/hub/v2/status" or "/api/hub/v2/devices" or "/api/hub/v2/runs"||Suffix(path,"/api/hub/actions/")||Suffix(path,"/api/hub/v2/actions/");
        if(method!="POST")return false;
        if(path is "/operator-stop" or "/api/hub/actions" or "/api/hub/selection" or "/api/hub/v2/actions" or "/api/hub/v2/selection")return true;
        var parts=path.Split('/');return parts.Length==5&&parts[1]=="api"&&parts[2]=="captures"&&Segment(parts[3])&&parts[4] is "start" or "stop";
    }
    bool Preflight(HttpContext c,string path)
    {
        var method=c.Request.Headers["Access-Control-Request-Method"];
        if(method.Count!=1||method[0] is not ("GET" or "POST")||!AllowedRoute(method[0]!,path)||c.Request.QueryString.HasValue||c.Request.Headers.ContainsKey("Authorization")||c.Request.ContentLength is >0||c.Request.Headers.ContainsKey("Transfer-Encoding"))return false;
        var headers=c.Request.Headers["Access-Control-Request-Headers"];
        if(headers.Count==0)return true;
        if(headers.Count!=1||headers[0] is not {} names||names.Length is <1 or >256)return false;
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var name in names.Split(',')){
            var value=name.Trim();
            if(!(value.Equals("Content-Type",StringComparison.OrdinalIgnoreCase)||value.Equals("Accept",StringComparison.OrdinalIgnoreCase)||value.Equals("Authorization",StringComparison.OrdinalIgnoreCase))||!seen.Add(value))return false;
        }
        return true;
    }
    void Cors(HttpContext c){c.Response.Headers["Access-Control-Allow-Origin"]=selection.Origin;c.Response.Headers.Vary="Origin";}
    static void NoStore(HttpContext c){c.Response.Headers.CacheControl="no-store";c.Response.Headers["Referrer-Policy"]="no-referrer";}
    static void Deny(HttpContext c,int status){NoStore(c);c.Response.StatusCode=status;}
}
