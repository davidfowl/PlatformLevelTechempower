﻿using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace ServerWithKestrel21
{
    public abstract class WebSocketConnection : HttpConnection
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

                Span<byte> mergedBytes = stackalloc byte[value.Length + _randomKey.Length];
                value.CopyTo(mergedBytes);
                _randomKey.AsSpan().CopyTo(mergedBytes.Slice(value.Length));
                // Compute the sha1, base64 encode in place, we need 28 bytes.
                // We make it 30 so we can add the \r\n for the header
                var target = new byte[30];
                _sha1.TryComputeHash(mergedBytes, target, out int written);
                var status = Base64.EncodeToUtf8InPlace(target, written, out written);

                Debug.Assert(status == OperationStatus.Done);
                Debug.Assert(written == 28);

                // Copy the crlf
                Buffer.BlockCopy(_crlf, 0, target, written, 2);

                _secWebSocketAcceptValue = new ReadOnlyMemory<byte>(target);

            }
        }

        public override async Task ProcessRequestAsync()
        {
            DoUpgrade();

            await Connection.Transport.Output.FlushAsync();

            using (var ws = WebSocketProtocol.CreateFromStream(new DuplexStream(Connection.Transport), isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromMinutes(2), new byte[14]))
            {
                await ProcessAsync(ws);
            }
        }

        protected abstract Task ProcessAsync(WebSocket websocket);

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

            // Clear the header
            _secWebSocketAcceptValue = ReadOnlyMemory<byte>.Empty;

            // End of headers
            writer.Write(_crlf);
            writer.Commit();
        }
    }
}
