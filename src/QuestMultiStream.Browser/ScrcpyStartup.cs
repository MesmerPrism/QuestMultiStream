using System.Net;
using System.Net.Sockets;
namespace QuestMultiStream.Browser;

// The second pinned scrcpy socket is owned only as a lifecycle signal. Its
// stream is never exposed: this host cannot serialize or send input events.
public sealed class ScrcpyConnection : IDisposable
{
    readonly TcpClient video;
    TcpClient? lifecycle;
    readonly CancellationTokenRegistration stopRegistration;
    internal ScrcpyConnection(TcpClient video, TcpClient lifecycle, CancellationToken stop)
    {
        this.video=video;this.lifecycle=lifecycle;
        stopRegistration=stop.Register(CloseLifecycle);
    }
    public NetworkStream GetStream()=>video.GetStream();
    void CloseLifecycle()=>Interlocked.Exchange(ref lifecycle,null)?.Dispose();
    public void Dispose(){CloseLifecycle();video.Dispose();stopRegistration.Dispose();}
}

public static class ScrcpyStartup
{
    // Retry only transport readiness of this ONE server launch, before its dummy byte.
    public static async Task<ScrcpyConnection> Connect(int port, Func<bool> serverExited, CancellationToken deadline, Action<string> observed)
    {
        while (true)
        {
            deadline.ThrowIfCancellationRequested();
            if (serverExited()) throw new IOException("Selected server exited before readiness");
            var client = new TcpClient();var dummyReady=false;
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port, deadline);
                var dummy = new byte[1]; var n = await client.GetStream().ReadAsync(dummy, deadline);
                if (n == 1)
                {
                    if (dummy[0] != 0) throw new InvalidDataException("Bad forward readiness byte");
                    dummyReady=true;
                    // DesktopConnection v3.3.4 accepts video then control before
                    // publishing metadata. After this actual dummy byte, finish
                    // the same server's lifecycle handshake even if Stop raced
                    // readiness, then immediately close it for an already-set Stop.
                    // There is no effect replay, input write or new deadline here.
                    var lifecycle=new TcpClient();
                    try {
                        await lifecycle.ConnectAsync(IPAddress.Loopback,port);
                        var connection=new ScrcpyConnection(client,lifecycle,deadline);
                        try {observed("lifecycle-connected");observed("dummy-ready");return connection;}
                        catch {connection.Dispose();throw;}
                    } catch {lifecycle.Dispose();throw;}
                }
                observed("forward-before-listener-eof");
            }
            catch (SocketException e) when (!dummyReady && (e.SocketErrorCode is SocketError.ConnectionRefused or SocketError.ConnectionReset))
            { observed(e.SocketErrorCode.ToString()); }
            catch { client.Dispose(); throw; }
            client.Dispose(); await Task.Delay(100, deadline);
        }
    }
}
