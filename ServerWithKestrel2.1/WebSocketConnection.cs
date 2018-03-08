using System;
using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace ServerWithKestrel21
{
    public class WebSocketConnection : HttpConnection
    {
        private static AsciiString _crlf = "\r\n";
        private static AsciiString _http101 = "HTTP/1.1 101 Switching Protocols\r\n";
        private static AsciiString _headerServer = "Server: Custom";
        private static AsciiString _headerConnectionUpgrade = "Connection: Upgrade\r\n";
        private static AsciiString _headerUpgradeWebSocket = "Upgrade: websocket\r\n";
        private static AsciiString _secWebSocketAccept = "Sec-WebSocket-Accept: ";

        private static SHA1 _sha1 = SHA1.Create();

        private static AsciiString _randomKey = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private static class HeaderNames
        {
            public static AsciiString SecWebSocketKey = "Sec-WebSocket-Key";
        }

        private static readonly DateHeaderValueManager _dateHeaderValueManager = new DateHeaderValueManager();

        private ReadOnlyMemory<byte> _secWebSocketAcceptValue;

        public override void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
        {
            // Everything here is websockets!
        }

        public override void OnHeader(Span<byte> name, Span<byte> value)
        {
            // Not case insensitive
            if (name.SequenceEqual(HeaderNames.SecWebSocketKey))
            {
                // "The value of this header field is constructed by concatenating /key/, defined above in step 4
                // in Section 4.2.2, with the string "258EAFA5-E914-47DA-95CA-C5AB0DC85B11", taking the SHA-1 hash of
                // this concatenated value to obtain a 20-byte value and base64-encoding"
                // https://tools.ietf.org/html/rfc6455#section-4.2.2

                byte[] mergedBytes = new byte[value.Length + _randomKey.Length];
                value.CopyTo(mergedBytes);
                _randomKey.AsSpan().CopyTo(mergedBytes.AsSpan().Slice(value.Length));
                byte[] hashedBytes = _sha1.ComputeHash(mergedBytes);
                int maxLength = Base64.GetMaxEncodedToUtf8Length(hashedBytes.Length);
                byte[] target = new byte[maxLength];
                var status = Base64.EncodeToUtf8(hashedBytes, target, out int consumed, out int written);

                _secWebSocketAcceptValue = new ReadOnlyMemory<byte>(target, 0, written);
            }
        }

        public override async Task ProcessRequestAsync()
        {
            DoUpgrade();

            await Connection.Transport.Output.FlushAsync();

            using (var ws = WebSocketProtocol.CreateFromStream(new DuplexStream(Connection.Transport), isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromMinutes(2)))
            {
                await ProcessAsync(ws);
            }
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

        private void DoUpgrade()
        {
            var writer = new BufferWriter<PipeWriter>(Connection.Transport.Output);

            // HTTP 1.1 OK
            writer.Write(_http101);

            // Server headers
            writer.Write(_headerServer);

            // Date header
            writer.Write(_dateHeaderValueManager.GetDateHeaderValues().Bytes);
            writer.Write(_crlf);

            // Connection: Upgrade
            writer.Write(_headerConnectionUpgrade);

            // Upgrade: websocket
            writer.Write(_headerUpgradeWebSocket);

            // Sec-WebSocket-Key
            writer.Write(_secWebSocketAccept);
            writer.Write(_secWebSocketAcceptValue.Span);
            writer.Write(_crlf);

            // End of headers
            writer.Write(_crlf);
            writer.Commit();
        }
    }
}
