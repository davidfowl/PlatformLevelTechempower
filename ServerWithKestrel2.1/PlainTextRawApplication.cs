using System;
using System.Buffers;
using System.Collections;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Protocols;
// The Kestrel's HttpParser is pubternal
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.Extensions.Logging;

namespace ServerWithKestrel21
{
    public class PlainTextRawApplication : IHttpHeadersHandler, IHttpRequestLineHandler
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
        private static readonly HttpParser<PlainTextRawApplication> _parser = new HttpParser<PlainTextRawApplication>();

        private static AsciiString _plainTextBody = "Hello, World!";

        private static class Paths
        {
            public static AsciiString Plaintext = "/plaintext";
            public static AsciiString Json = "/json";
        }

        private State _state;

        private HttpMethod _method;
        private byte[] _path;

        // Paths < 256 bytes are copied to here and this is reused per connection
        private byte[] _pathBuffer = new byte[256];
        private int _pathLength;

        public async Task ExecuteAsync(ConnectionContext connection)
        {
            try
            {
                while (true)
                {
                    var result = await connection.Transport.Input.ReadAsync();
                    var inputBuffer = result.Buffer;
                    var consumed = inputBuffer.Start;
                    var examined = inputBuffer.End;

                    try
                    {
                        if (inputBuffer.IsEmpty && result.IsCompleted)
                        {
                            break;
                        }

                        ParseHttpRequest(inputBuffer, out consumed, out examined);

                        if (_state != State.Body && result.IsCompleted)
                        {
                            // Bad request
                            break;
                        }

                        if (_state == State.Body)
                        {
                            if (_method == HttpMethod.Get)
                            {
                                HandleRequest(connection.Transport.Output);
                            }
                            else
                            {
                                Default(connection.Transport.Output);
                            }

                            await connection.Transport.Output.FlushAsync();

                            _path = null;

                            _state = State.StartLine;
                        }
                    }
                    finally
                    {
                        connection.Transport.Input.AdvanceTo(consumed, examined);
                    }
                }

                connection.Transport.Input.Complete();
            }
            catch (Exception ex)
            {
                connection.Transport.Input.Complete(ex);
            }
            finally
            {
                connection.Transport.Output.Complete();
            }
        }

        private void HandleRequest(PipeWriter pipeWriter)
        {
            Span<byte> path;

            if (_path != null)
            {
                path = _path;
            }
            else
            {
                path = new Span<byte>(_pathBuffer, 0, _pathLength);
            }

            if (path.StartsWith(Paths.Plaintext))
            {
                PlainText(pipeWriter);
            }
            else if (path.StartsWith(Paths.Json))
            {
                // Json(pipeWriter);
            }
            else
            {
                Default(pipeWriter);
            }
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

        /*private static void Json(PipeWriter pipeWriter)
        {
            var writer = OutputWriter.Create(pipeWriter);

            // HTTP 1.1 OK
            writer.Write(_http11OK);

            // Server headers
            writer.Write(_headerServer);

            // Date header
            writer.Write(_dateHeaderValueManager.GetDateHeaderValues().Bytes);
            writer.Write(_crlf);

            // Content-Type header
            writer.Write(_headerContentTypeJson);
            writer.Write(_crlf);

            var jsonPayload = JsonSerializer.SerializeUnsafe(new { message = "Hello, World!" });

            // Content-Length header
            writer.Write(_headerContentLength);
            PipelineExtensions.WriteNumeric(ref writer, (ulong)jsonPayload.Count);
            writer.Write(_crlf);

            // End of headers
            writer.Write(_crlf);

            // Body
            writer.Write(jsonPayload.Array, jsonPayload.Offset, jsonPayload.Count);
        }*/

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

        private void ParseHttpRequest(ReadOnlySequence<byte> inputBuffer, out SequencePosition consumed, out SequencePosition examined)
        {
            consumed = inputBuffer.Start;
            examined = inputBuffer.End;

            if (_state == State.StartLine)
            {
                if (_parser.ParseRequestLine(this, inputBuffer, out consumed, out examined))
                {
                    _state = State.Headers;
                    inputBuffer = inputBuffer.Slice(consumed);
                }
            }

            if (_state == State.Headers)
            {
                if (_parser.ParseHeaders(this, inputBuffer, out consumed, out examined, out int consumedBytes))
                {
                    _state = State.Body;
                }
            }
        }

        public void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
        {
            _method = method;

            if (path.TryCopyTo(_pathBuffer))
            {
                _pathLength = path.Length;
            }
            else // path > 256
            {
                _path = path.ToArray();
            }
        }

        public void OnHeader(Span<byte> name, Span<byte> value)
        {
        }

        private enum State
        {
            StartLine,
            Headers,
            Body
        }
    }
}
