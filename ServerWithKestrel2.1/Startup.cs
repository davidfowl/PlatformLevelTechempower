using System;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace ServerWithKestrel21
{
    public class Startup
    {
        public void Configure(IApplicationBuilder app)
        {
            app.UseFileServer();

            app.UseWebSockets();

            app.Run(async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    return;
                }

                using (var websocket = await context.WebSockets.AcceptWebSocketAsync())
                {
                    await ProcessAsync(websocket);
                }
            });
        }

        private async Task ProcessAsync(WebSocket websocket)
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
