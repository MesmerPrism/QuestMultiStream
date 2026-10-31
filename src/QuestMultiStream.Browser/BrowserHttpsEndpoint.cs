using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http.Features;

namespace QuestMultiStream.Browser;

internal sealed record BrowserHttpsSelection(string ListenAddress,int Port,string Origin,string CertificatePath,string PasswordFile)
{
    internal void Validate()
    {
        if(!IPAddress.TryParse(ListenAddress,out var address)||address.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork||address.ToString()!=ListenAddress)throw new InvalidDataException("Explicit private IPv4 listener required");
        var b=address.GetAddressBytes();
        if(!(address.Equals(IPAddress.Loopback)||b[0]==10||b[0]==172&&b[1] is >=16 and <=31||b[0]==192&&b[1]==168)||Port is <1024 or >65535)throw new InvalidDataException("Private or loopback HTTPS listener and operator port required");
        if(!Uri.TryCreate(Origin,UriKind.Absolute,out var origin)||origin.Scheme!="https"||origin.Port!=Port||origin.UserInfo.Length!=0||origin.AbsolutePath!="/"||origin.Query.Length!=0||origin.Fragment.Length!=0||origin.GetLeftPart(UriPartial.Authority)!=Origin)throw new InvalidDataException("Exact HTTPS origin with matching port required");
        if(!Path.IsPathFullyQualified(CertificatePath)||!Path.IsPathFullyQualified(PasswordFile))throw new InvalidDataException("Private absolute certificate and password file paths required");
    }
}

internal sealed class BrowserHttpsEndpoint(BrowserHttpsSelection selection,X509Certificate2 certificate):IDisposable
{
    internal BrowserHttpsSelection Selection=>selection;
    internal X509Certificate2 Certificate=>certificate;
    internal static BrowserHttpsEndpoint? Load(string? path)
    {
        if(path is null)return null;
        if(!Path.IsPathFullyQualified(path))throw new InvalidDataException("Absolute private HTTPS selection required");
        using var file=File.OpenRead(path);if(file.Length>4096)throw new InvalidDataException("Bounded HTTPS selection required");
        var bytes=new byte[(int)file.Length];file.ReadExactly(bytes);var selection=OwnerJson.Read<BrowserHttpsSelection>(bytes);selection.Validate();
        var password=File.ReadAllText(selection.PasswordFile).TrimEnd('\r','\n');
        var certificate=X509CertificateLoader.LoadPkcs12FromFile(selection.CertificatePath,password,X509KeyStorageFlags.DefaultKeySet);
        if(!certificate.HasPrivateKey){certificate.Dispose();throw new InvalidDataException("HTTPS certificate requires its private key");}
        return new(selection,certificate);
    }
    internal void Configure(WebApplicationBuilder builder,int loopbackPort)
    {
        if(IPAddress.Parse(selection.ListenAddress).Equals(IPAddress.Loopback)&&selection.Port==loopbackPort)throw new InvalidDataException("Separate HTTPS listener port required");
        // No forwarded-host/proto trust: direct Kestrel transport selects the authority.
        builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o=>o.ForwardedHeaders=Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.None);
        builder.WebHost.ConfigureKestrel(o=>{
            // Listen APIs otherwise accumulate appsettings/environment endpoints.
            // This opt-in owns exactly the two selected sockets, without reload binding.
            o.Configure(new ConfigurationBuilder().Build(),reloadOnChange:false);
            o.Listen(IPAddress.Loopback,loopbackPort);
            o.Listen(IPAddress.Parse(selection.ListenAddress),selection.Port,listener=>listener.UseHttps(certificate));
        });
    }
    internal static void Guard(WebApplication app,string localOrigin,BrowserHttpsSelection? selection)
    {
        if(selection is null)return;
        app.Use(async(context,next)=>{
            if(!Allowed(context,localOrigin,selection)){context.Response.StatusCode=403;return;}
            await next(context);
        });
    }
    internal static bool IsSelectedTls(HttpContext context,BrowserHttpsSelection selection)=>
        context.Features.Get<ITlsConnectionFeature>() is not null&&context.Connection.LocalPort==selection.Port&&
        context.Connection.LocalIpAddress?.Equals(IPAddress.Parse(selection.ListenAddress))==true;
    internal static bool Allowed(HttpContext context,string localOrigin,BrowserHttpsSelection? selection)
    {
        if(selection is null)return true; // Omission preserves the existing loopback host.
        if(IsSelectedTls(context,selection))return context.Request.Host.Value==new Uri(selection.Origin).Authority&&!context.Request.Path.StartsWithSegments("/api/owner");
        return context.Features.Get<ITlsConnectionFeature>() is null&&context.Request.Scheme=="http"&&
            context.Connection.LocalIpAddress?.Equals(IPAddress.Loopback)==true&&context.Connection.LocalPort==new Uri(localOrigin).Port&&context.Request.Host.Value==new Uri(localOrigin).Authority;
    }
    internal static string Origin(HttpContext context,string localOrigin,BrowserHttpsSelection? selection)=>
        selection is not null&&IsSelectedTls(context,selection)?selection.Origin:localOrigin;
    internal static bool SameOrigin(HttpContext context,string localOrigin,BrowserHttpsSelection? selection)=>context.Request.Headers.Origin==Origin(context,localOrigin,selection);
    public void Dispose()=>certificate.Dispose();
}
