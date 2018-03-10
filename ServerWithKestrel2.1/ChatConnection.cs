using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace ServerWithKestrel21
{
    public class ChatConnection : WebSocketConnection
    {
        protected override async Task ProcessAsync(WebSocket websocket)
        {
            while (true)
            {
                // Bufferless async wait (well it's like 14 bytes)
                var result = await websocket.ReceiveAsync(Memory<byte>.Empty, default);

                // If we got a close then send the close frame back
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await websocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", default);
                    return;
                }
                else
                {
                    // Rent 4K
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
                    try
                    {
                        var memory = buffer.AsMemory();
                        // This should always be synchronous
                        var task = websocket.ReceiveAsync(memory, default);

                        Debug.Assert(task.IsCompleted);

                        result = task.GetAwaiter().GetResult();
                        await websocket.SendAsync(memory.Slice(0, result.Count), result.MessageType, result.EndOfMessage, default);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }
        }
    }
}
