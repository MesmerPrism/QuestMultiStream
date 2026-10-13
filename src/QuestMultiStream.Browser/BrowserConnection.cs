using System.Net.WebSockets;
namespace QuestMultiStream.Browser;

public static class BrowserConnection
{
    public static async Task ObserveClose(WebSocket socket, CancellationToken stop, Action closed)
    {
        var buffer = new byte[1];
        var frame = await socket.ReceiveAsync(buffer.AsMemory(), stop);
        if (frame.MessageType != WebSocketMessageType.Close)
            throw new InvalidDataException("Viewer sent unsupported data; this is a read-only channel");
        closed(); // Actual close frame, not absence of decoded browser frames.
    }
}
