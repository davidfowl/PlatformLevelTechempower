using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace ServerWithKestrel21
{
    public class ChatConnection : WebSocketConnection
    {
        protected override async Task ProcessAsync(WebSocket websocket)
        {
            Memory<byte> buffer = new byte[4096];

            while (true)
            {
                var result = await websocket.ReceiveAsync(buffer, default);

                switch (result.MessageType)
                {
                    case WebSocketMessageType.Close:
                        await websocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", default);
                        return;
                    case WebSocketMessageType.Binary:
                    case WebSocketMessageType.Text:
                        await websocket.SendAsync(buffer.Slice(0, result.Count), result.MessageType, result.EndOfMessage, default);
                        break;
                    default:
                        break;
                }
            }
        }

    }
}
