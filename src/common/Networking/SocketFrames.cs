// BonkLink edition addition, 2026-09-12. Distributed under GPL-2.0; see LICENSE.
using System.Net.WebSockets;
namespace MegabonkTogether.Common.Networking;
public static class SocketFrames
{
    public static async Task<byte[]> ReadBinary(WebSocket socket, CancellationToken token, int maxBytes = 65536)
    {
        var chunk = new byte[4096];
        using var message = new MemoryStream();
        while (true)
        {
            var frame = await socket.ReceiveAsync(new ArraySegment<byte>(chunk), token);
            if (frame.MessageType == WebSocketMessageType.Close) throw new EndOfStreamException("Matchmaking connection closed");
            if (frame.MessageType != WebSocketMessageType.Binary) throw new InvalidDataException("Expected a binary matchmaking message");
            if (message.Length + frame.Count > maxBytes) throw new InvalidDataException("Matchmaking message is too large");
            message.Write(chunk, 0, frame.Count);
            if (frame.EndOfMessage) return message.ToArray();
        }
    }
}
