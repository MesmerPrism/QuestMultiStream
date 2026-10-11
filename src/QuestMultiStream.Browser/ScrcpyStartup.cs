using System.Net;
using System.Net.Sockets;
namespace QuestMultiStream.Browser;

public static class ScrcpyStartup
{
    // Retry only transport readiness of this ONE server launch, before its dummy byte.
    public static async Task<TcpClient> Connect(int port, Func<bool> serverExited, CancellationToken deadline, Action<string> observed)
    {
        while (true)
        {
            deadline.ThrowIfCancellationRequested();
            if (serverExited()) throw new IOException("Selected server exited before readiness");
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, port, deadline);
                var dummy = new byte[1]; var n = await client.GetStream().ReadAsync(dummy, deadline);
                if (n == 1)
                {
                    if (dummy[0] != 0) throw new InvalidDataException("Bad forward readiness byte");
                    observed("dummy-ready"); return client;
                }
                observed("forward-before-listener-eof");
            }
            catch (SocketException e) when (e.SocketErrorCode is SocketError.ConnectionRefused or SocketError.ConnectionReset)
            { observed(e.SocketErrorCode.ToString()); }
            catch { client.Dispose(); throw; }
            client.Dispose(); await Task.Delay(100, deadline);
        }
    }
}
