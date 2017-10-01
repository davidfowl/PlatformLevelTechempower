using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Utf8Json;

namespace PlatformLevelTechempower
{
    public class PlainTextRawApplication : IConnectionHandler, IServerApplication
    {
        private static readonly byte[] _crlf = Encoding.UTF8.GetBytes("\r\n");
        private static readonly byte[] _http11OK = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\n");
        private static readonly byte[] _headerServer = Encoding.UTF8.GetBytes("Server: Custom");
        private static readonly byte[] _headerDate = Encoding.UTF8.GetBytes("Date: ");
        private static readonly byte[] _headerContentLength = Encoding.UTF8.GetBytes("Content-Length: ");
        private static readonly byte[] _headerContentLengthZero = Encoding.UTF8.GetBytes("Content-Length: 0");
        private static readonly byte[] _headerContentTypeText = Encoding.UTF8.GetBytes("Content-Type: text/plain");
        private static readonly byte[] _headerContentTypeJson = Encoding.UTF8.GetBytes("Content-Type: application/json");

        private static readonly DateHeaderValueManager _dateHeaderValueManager = new DateHeaderValueManager();
        private static readonly HttpParser<HttpConnectionContext> _parser = new HttpParser<HttpConnectionContext>();

        private static readonly byte[] _plainTextBody = Encoding.UTF8.GetBytes("Hello, World!");

        public async Task RunAsync(ITransportFactory transportFactory, IEndPointInformation endPointInformation, ApplicationLifetime lifetime)
        {
            Console.CancelKeyPress += (sender, e) => lifetime.StopApplication();

            var transport = transportFactory.Create(endPointInformation, this);

            await transport.BindAsync();

            Console.WriteLine($"Server ({nameof(PlainTextRawApplication)}) listening on http://{endPointInformation.IPEndPoint}");

            lifetime.ApplicationStopping.WaitHandle.WaitOne();

            await transport.UnbindAsync();
            await transport.StopAsync();
        }

        public IConnectionContext OnConnection(IConnectionInformation connectionInfo)
        {
            var inputOptions = new PipeOptions { WriterScheduler = connectionInfo.InputWriterScheduler };
            var outputOptions = new PipeOptions { ReaderScheduler = connectionInfo.OutputReaderScheduler };

            var context = new HttpConnectionContext
            {
                ConnectionId = Guid.NewGuid().ToString(),
                Input = connectionInfo.PipeFactory.Create(inputOptions),
                Output = connectionInfo.PipeFactory.Create(outputOptions),
            };

            _ = context.ExecuteAsync();

            return context;
        }

        private static class Paths
        {
            public static readonly byte[] Plaintext = Encoding.ASCII.GetBytes("/plaintext");
            public static readonly byte[] Json = Encoding.ASCII.GetBytes("/json");
        }

        private class HttpConnectionContext : IConnectionContext, IHttpHeadersHandler, IHttpRequestLineHandler
        {
            private State _state;

            private HttpMethod _method;
            private byte[] _path;

            // Paths < 256 bytes are copied to here and this is reused per connection
            private byte[] _pathBuffer = new byte[256];
            private int _pathLength;

            public string ConnectionId { get; set; }

            public IPipe Input { get; set; }

            public IPipe Output { get; set; }

            IPipeWriter IConnectionContext.Input => Input.Writer;

            IPipeReader IConnectionContext.Output => Output.Reader;

            public void Abort(Exception ex)
            {

            }

            public void OnConnectionClosed(Exception ex)
            {

            }

            public async Task ExecuteAsync()
            {
                try
                {
                    while (true)
                    {
                        var result = await Input.Reader.ReadAsync();
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
                                var outputBuffer = Output.Writer.Alloc();

                                if (_method == HttpMethod.Get)
                                {
                                    HandleRequest(ref outputBuffer);
                                }
                                else
                                {
                                    Default(ref outputBuffer);
                                }

                                await outputBuffer.FlushAsync();

                                _path = null;

                                _state = State.StartLine;
                            }
                        }
                        finally
                        {
                            Input.Reader.Advance(consumed, examined);
                        }
                    }

                    Input.Reader.Complete();
                }
                catch (Exception ex)
                {
                    Input.Reader.Complete(ex);
                }
                finally
                {
                    Output.Writer.Complete();
                }
            }

            private void HandleRequest(ref WritableBuffer outputBuffer)
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
                    PlainText(ref outputBuffer);
                }
                else if (path.StartsWith(Paths.Json))
                {
                    Json(ref outputBuffer);
                }
                else
                {
                    Default(ref outputBuffer);
                }
            }

            private static void Default(ref WritableBuffer outputBuffer)
            {
                var writer = new WritableBufferWriter(outputBuffer);

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
            }

            private static void Json(ref WritableBuffer outputBuffer)
            {
                var writer = new WritableBufferWriter(outputBuffer);

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
            }

            private static void PlainText(ref WritableBuffer outputBuffer)
            {
                var writer = new WritableBufferWriter(outputBuffer);
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
                PipelineExtensions.WriteNumeric(ref writer, (ulong)_plainTextBody.Length);
                writer.Write(_crlf);

                // End of headers
                writer.Write(_crlf);

                // Body
                writer.Write(_plainTextBody);
            }

            private void ParseHttpRequest(ReadableBuffer inputBuffer, out ReadCursor consumed, out ReadCursor examined)
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
}
