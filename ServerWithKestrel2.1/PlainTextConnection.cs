﻿using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace ServerWithKestrel21
{
    public class PlainTextConnection : HttpConnection
    {
        private static AsciiString _crlf = "\r\n";
        private static AsciiString _http11OK = "HTTP/1.1 200 OK\r\n";
        private static AsciiString _headerServer = "Server: Custom";
        private static AsciiString _headerDate = "Date: ";
        private static AsciiString _headerContentLength = "Content-Length: ";
        private static AsciiString _headerContentLengthZero = "Content-Length: 0";
        private static AsciiString _headerContentTypeText = "Content-Type: text/plain";
        private static AsciiString _headerContentTypeJson = "Content-Type: application/json";

        private static readonly DateHeaderValueManager _dateHeaderValueManager = new DateHeaderValueManager();

        private bool _isPlainText;

        private static AsciiString _plainTextBody = "Hello, World!";

        private static class Paths
        {
            public static AsciiString Plaintext = "/plaintext";
        }

        public override void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
        {
            _isPlainText = method == HttpMethod.Get && path.StartsWith(Paths.Plaintext);
        }

        public override void OnHeader(Span<byte> name, Span<byte> value)
        {
        }

        public override async Task ProcessRequestAsync()
        {
            if (_isPlainText)
            {
                PlainText(Connection.Transport.Output);
            }
            else
            {
                Default(Connection.Transport.Output);
            }

            await Connection.Transport.Output.FlushAsync();
        }

        private static void Default(PipeWriter pipeWriter)
        {
            var writer = new BufferWriter<PipeWriter>(pipeWriter);

            // HTTP 1.1 OK
            writer.Write(_http11OK);

            // Server headers
            writer.Write(_headerServer);

            // Date header
            writer.Write(_dateHeaderValueManager.GetDateHeaderValues().Bytes);
            writer.Write(_crlf);

            // Content-Length 0
            writer.Write(_headerContentLengthZero);
            writer.Write(_crlf);

            // End of headers
            writer.Write(_crlf);
            writer.Commit();
        }

        private static void PlainText(PipeWriter pipeWriter)
        {
            var writer = new BufferWriter<PipeWriter>(pipeWriter);
            // HTTP 1.1 OK
            writer.Write(_http11OK);

            // Server headers
            writer.Write(_headerServer);

            // Date header
            writer.Write(_dateHeaderValueManager.GetDateHeaderValues().Bytes);
            writer.Write(_crlf);

            // Content-Type header
            writer.Write(_headerContentTypeText);
            writer.Write(_crlf);

            // Content-Length header
            writer.Write(_headerContentLength);
            writer.WriteNumeric((ulong)_plainTextBody.Length);
            writer.Write(_crlf);

            // End of headers
            writer.Write(_crlf);

            // Body
            writer.Write(_plainTextBody);
            writer.Commit();
        }
    }
}
