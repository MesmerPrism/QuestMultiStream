using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace QuestMultiStream.Browser;

internal static class BrowserHttpsTests
{
    internal static void Run()
    {
        var selection=new BrowserHttpsSelection("192.168.20.10",44318,"https://controls.example.test:44318",Path.GetFullPath("fixture.pfx"),Path.GetFullPath("fixture-password.txt"));
        selection.Validate();
        foreach(var invalid in new[]{selection with {ListenAddress="0.0.0.0"},selection with {ListenAddress="8.8.8.8"},selection with {ListenAddress="::1"},selection with {Port=443},selection with {Origin="http://controls.example.test:44318"},selection with {Origin="https://controls.example.test:44318/path"},selection with {Origin="https://controls.example.test:44318/#token=secret"},selection with {Origin="https://controls.example.test:44319"},selection with {CertificatePath="relative.pfx"},selection with {PasswordFile="relative.txt"}})
        {try{invalid.Validate();throw new Exception("Invalid HTTPS selection accepted");}catch(InvalidDataException){}}
        const string local="http://127.0.0.1:43118";
        DefaultHttpContext Context(bool tls,string host,string path="/"){
            var c=new DefaultHttpContext();c.Request.Scheme=tls?"https":"http";c.Request.Host=new HostString(host);c.Request.Path=path;
            c.Connection.LocalIpAddress=IPAddress.Parse(tls?selection.ListenAddress:"127.0.0.1");c.Connection.LocalPort=tls?selection.Port:43118;
            if(tls)c.Features.Set<ITlsConnectionFeature>(new TlsConnectionFeature());return c;
        }
        var phone=Context(true,"controls.example.test:44318");
        if(!BrowserHttpsEndpoint.Allowed(phone,local,selection)||BrowserHttpsEndpoint.Origin(phone,local,selection)!=selection.Origin)throw new Exception("Selected TLS authority rejected");
        phone.Request.Headers.Origin=selection.Origin;
        if(!BrowserHttpsEndpoint.SameOrigin(phone,local,selection))throw new Exception("Exact TLS origin rejected");
        phone.Request.Headers.Origin="https://other.example.test:44318";
        if(BrowserHttpsEndpoint.SameOrigin(phone,local,selection))throw new Exception("Foreign TLS origin accepted");
        phone.Request.Headers.Origin=local;
        if(BrowserHttpsEndpoint.SameOrigin(phone,local,selection))throw new Exception("HTTP origin crossed TLS listener");
        phone.Request.Host=new HostString("other.example.test:44318");
        if(BrowserHttpsEndpoint.Allowed(phone,local,selection))throw new Exception("Foreign TLS host accepted");
        phone=Context(true,"controls.example.test:44318","/api/owner/captures");
        if(BrowserHttpsEndpoint.Allowed(phone,local,selection))throw new Exception("Private owner path exposed on TLS");
        phone=Context(true,"controls.example.test:44318");phone.Connection.LocalPort=44319;
        if(BrowserHttpsEndpoint.Allowed(phone,local,selection))throw new Exception("Unselected TLS endpoint accepted");
        var loopback=Context(false,"127.0.0.1:43118","/api/owner/captures");
        if(!BrowserHttpsEndpoint.Allowed(loopback,local,selection))throw new Exception("Private loopback route changed");
        loopback.Request.Headers["X-Forwarded-Host"]="controls.example.test:44318";loopback.Request.Headers["X-Forwarded-Proto"]="https";loopback.Request.Headers["Forwarded"]="host=controls.example.test:44318;proto=https";
        if(BrowserHttpsEndpoint.Origin(loopback,local,selection)!=local)throw new Exception("Forwarded authority trusted");
        loopback.Request.Scheme="https";loopback.Request.Host=new HostString("controls.example.test:44318");
        if(BrowserHttpsEndpoint.Allowed(loopback,local,selection))throw new Exception("HTTPS scheme without actual TLS accepted");
        if(!BrowserHttpsEndpoint.Allowed(Context(false,"arbitrary.invalid"),local,null))throw new Exception("Default loopback behavior changed without opt-in");
        Console.WriteLine("PASS optional HTTPS private address/origin selection, direct TLS endpoint/Host/Origin isolation, forwarded headers ignored, public owner path denied, default loopback unchanged");
    }

    internal static async Task Http(int httpPort,int tlsPort)
    {
        if(httpPort is <1024 or >65535||tlsPort is <1024 or >65535||httpPort==tlsPort)throw new InvalidDataException("Two distinct explicit fixture ports required");
        // Ephemeral test CA trusted only by this client. No OS trust, callback bypass or LAN bind.
        using var caKey=RSA.Create(2048);var caRequest=new CertificateRequest("CN=QMS ephemeral fixture CA",caKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign,true));
        using var ca=caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));
        using var key=RSA.Create(2048);var request=new CertificateRequest("CN=QMS ephemeral loopback",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var san=new SubjectAlternativeNameBuilder();san.AddIpAddress(IPAddress.Loopback);san.AddDnsName("other.example.test");request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.1")},true));
        using var publicCertificate=request.Create(ca,DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddMinutes(30),RandomNumberGenerator.GetBytes(16));
        var selection=new BrowserHttpsSelection("127.0.0.1",tlsPort,$"https://127.0.0.1:{tlsPort}",Path.GetFullPath("fixture.pfx"),Path.GetFullPath("fixture-password.txt"));selection.Validate();
        using var signed=publicCertificate.CopyWithPrivateKey(key);
        using var endpoint=new BrowserHttpsEndpoint(selection,X509CertificateLoader.LoadPkcs12(signed.Export(X509ContentType.Pkcs12),null));
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=Path.GetFullPath("src/QuestMultiStream.Browser")});
        builder.Configuration.Sources.Clear();builder.Logging.ClearProviders();endpoint.Configure(builder,httpPort);
        await using var app=builder.Build();var local=$"http://127.0.0.1:{httpPort}";
        BrowserHttpsEndpoint.Guard(app,local,selection);app.UseWebSockets();app.UseStaticFiles();
        bool Auth(HttpContext c)=>c.Request.Query["token"]=="fixture";
        using var lane=new SemaphoreSlim(1,1);OwnerV2Routes.Map(app,null,Auth,c=>BrowserHttpsEndpoint.Origin(c,local,selection),lane,()=>true);
        app.MapGet("/api/owner/fixture-private",()=>Results.Ok());
        app.Map("/fixture-ws",async(HttpContext c)=>{
            if(!Auth(c)||!BrowserHttpsEndpoint.SameOrigin(c,local,selection)||!c.WebSockets.IsWebSocketRequest){c.Response.StatusCode=403;return;}
            using var ws=await c.WebSockets.AcceptWebSocketAsync();
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure,"fixture only",c.RequestAborted);
        });
        using var handler=new SocketsHttpHandler{UseProxy=false};
        handler.SslOptions.CertificateChainPolicy=new X509ChainPolicy{TrustMode=X509ChainTrustMode.CustomRootTrust,RevocationMode=X509RevocationMode.NoCheck};
        handler.SslOptions.CertificateChainPolicy.CustomTrustStore.Add(ca);
        using var client=new HttpClient(handler,false){Timeout=TimeSpan.FromSeconds(10)};
        try{
            await app.StartAsync();
            if((await client.GetAsync(selection.Origin+"/manifest.webmanifest")).StatusCode!=HttpStatusCode.OK)throw new Exception("TLS static manifest failed");
            if((await client.GetAsync(selection.Origin+"/api/hub/v2/status")).StatusCode!=HttpStatusCode.Unauthorized)throw new Exception("TLS bearer authentication bypassed");
            if((await client.GetAsync(selection.Origin+"/api/hub/v2/status?token=fixture")).StatusCode!=HttpStatusCode.ServiceUnavailable)throw new Exception("TLS authenticated production route unavailable");
            async Task<HttpStatusCode> Post(string origin,string? suppliedOrigin,string token="fixture"){
                using var message=new HttpRequestMessage(HttpMethod.Post,origin+"/api/hub/v2/actions?token="+token){Content=new StringContent(JsonSerializer.Serialize(OwnerV2Tests.Action(),OwnerJson.Options),Encoding.UTF8,"application/json")};
                if(suppliedOrigin is not null)message.Headers.Add("Origin",suppliedOrigin);
                using var response=await client.SendAsync(message);return response.StatusCode;
            }
            if(await Post(selection.Origin,selection.Origin)!=HttpStatusCode.ServiceUnavailable)throw new Exception("Exact TLS Origin rejected by production action route");
            foreach(var origin in new[]{local,"https://other.example.test",null})if(await Post(selection.Origin,origin)!=HttpStatusCode.Unauthorized)throw new Exception("Foreign/missing TLS Origin accepted");
            if(await Post(selection.Origin,selection.Origin,"")!=HttpStatusCode.Unauthorized)throw new Exception("TLS action token bypassed");
            if(await Post(local,local)!=HttpStatusCode.ServiceUnavailable)throw new Exception("Existing loopback action origin changed");
            if((await client.GetAsync(selection.Origin+"/api/owner/fixture-private")).StatusCode!=HttpStatusCode.Forbidden)throw new Exception("Private owner path exposed to local TLS client");
            if((await client.GetAsync(local+"/api/owner/fixture-private")).StatusCode!=HttpStatusCode.OK)throw new Exception("Loopback owner path changed");
            using var wrongHost=new HttpRequestMessage(HttpMethod.Get,selection.Origin+"/manifest.webmanifest");wrongHost.Headers.Host="other.example.test:"+tlsPort;
            if((await client.SendAsync(wrongHost)).StatusCode!=HttpStatusCode.Forbidden)throw new Exception("Wrong TLS Host accepted");
            using var forwarded=new HttpRequestMessage(HttpMethod.Post,local+"/api/hub/v2/actions?token=fixture"){Content=new StringContent(JsonSerializer.Serialize(OwnerV2Tests.Action(),OwnerJson.Options),Encoding.UTF8,"application/json")};
            forwarded.Headers.Add("X-Forwarded-Proto","https");forwarded.Headers.Add("X-Forwarded-Host",new Uri(selection.Origin).Authority);forwarded.Headers.Add("Origin",selection.Origin);
            if((await client.SendAsync(forwarded)).StatusCode!=HttpStatusCode.Unauthorized)throw new Exception("Forwarded origin changed listener authority");
            using var invoker=new HttpMessageInvoker(handler,false);
            async Task Socket(string? origin,string token,bool expected){
                using var ws=new ClientWebSocket();if(origin is not null)ws.Options.SetRequestHeader("Origin",origin);
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try{await ws.ConnectAsync(new Uri($"wss://127.0.0.1:{tlsPort}/fixture-ws?token={token}"),invoker,timeout.Token);if(!expected)throw new Exception("Denied WSS client accepted");await ws.CloseAsync(WebSocketCloseStatus.NormalClosure,"fixture done",timeout.Token);}
                catch(WebSocketException)when(!expected){}
            }
            await Socket(selection.Origin,"fixture",true);await Socket(local,"fixture",false);await Socket(null,"fixture",false);await Socket(selection.Origin,"",false);
            using var ordinary=new HttpClient(new SocketsHttpHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(10)};
            try{await ordinary.GetAsync(selection.Origin+"/manifest.webmanifest");throw new Exception("Untrusted certificate accepted");}catch(HttpRequestException e)when(e.HttpRequestError==HttpRequestError.SecureConnectionError){}
            try{await client.GetAsync($"https://localhost:{tlsPort}/manifest.webmanifest");throw new Exception("Certificate SAN mismatch accepted");}catch(HttpRequestException e)when(e.HttpRequestError==HttpRequestError.SecureConnectionError){}
            Console.WriteLine("PASS real loopback TLS with client-local CA trust, untrusted/SAN rejection, static manifest, production v2 bearer/Host/Origin boundaries, private-owner isolation and authenticated WSS fixture; no device or OS trust effects");
        }finally{await app.StopAsync();}
    }
}


