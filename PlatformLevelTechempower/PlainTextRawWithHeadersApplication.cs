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
    public class PlainTextRawWithHeadersApplication : IConnectionHandler, IServerApplication
    {
        private static readonly byte[] _bytesHttpVersion11 = Encoding.ASCII.GetBytes("HTTP/1.1 ");
        private static readonly byte[] _headerServer = Encoding.UTF8.GetBytes("\r\nServer: Custom");
        private static readonly byte[] _plainTextBody = Encoding.UTF8.GetBytes("Hello, World!");
        private static readonly byte[] _bytesEndHeaders = Encoding.ASCII.GetBytes("\r\n\r\n");

        private static readonly DateHeaderValueManager _dateHeaderValueManager = new DateHeaderValueManager();
        private static readonly HttpParser<HttpConnectionContext> _parser = new HttpParser<HttpConnectionContext>();

        public async Task RunAsync(int port, int threadCount)
        {
            var lifetime = new ApplicationLifetime(NullLoggerFactory.Instance.CreateLogger<ApplicationLifetime>());

            Console.CancelKeyPress += (sender, e) => lifetime.StopApplication();

            var libuvOptions = new LibuvTransportOptions
            {
                ThreadCount = threadCount
            };
            var libuvTransport = new LibuvTransportFactory(
                Options.Create(libuvOptions),
                lifetime,
                NullLoggerFactory.Instance);

            var binding = new IPEndPointInformation(new System.Net.IPEndPoint(System.Net.IPAddress.Any, port));

            var transport = libuvTransport.Create(binding, this);
            await transport.BindAsync();

            Console.WriteLine($"Server (raw with headers) listening on http://*:{port} with {libuvOptions.ThreadCount} thread(s)");

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

            private FrameResponseHeaders ResponseHeaders = new FrameResponseHeaders();

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
                                ResponseHeaders.Reset();

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

            private void Default(ref WritableBuffer outputBuffer)
            {
                var writer = new WritableBufferWriter(outputBuffer);

                // HTTP 1.1 OK
                writer.Write(_bytesHttpVersion11);
                writer.Write(ReasonPhrases.ToStatusBytes(200, reasonPhrase: null));

                // Headers
                var values = _dateHeaderValueManager.GetDateHeaderValues();
                ResponseHeaders.SetRawDate(values.String, values.Bytes);
                ResponseHeaders.SetRawServer("Custom", _headerServer);
                ResponseHeaders.ContentLength = 0;

                // Write headers
                ResponseHeaders.CopyTo(ref writer);
                writer.Write(_bytesEndHeaders);
            }

            private void Json(ref WritableBuffer outputBuffer)
            {
                var writer = new WritableBufferWriter(outputBuffer);

                // HTTP 1.1 OK
                writer.Write(_bytesHttpVersion11);
                writer.Write(ReasonPhrases.ToStatusBytes(200, reasonPhrase: null));

                // Headers
                var values = _dateHeaderValueManager.GetDateHeaderValues();
                ResponseHeaders.SetRawDate(values.String, values.Bytes);
                ResponseHeaders.SetRawServer("Custom", _headerServer);
                ResponseHeaders.HeaderContentType = "application/json";
                var jsonPayload = JsonSerializer.SerializeUnsafe(new { message = "Hello, World!" });
                ResponseHeaders.ContentLength = jsonPayload.Count;

                // Write headers
                ResponseHeaders.CopyTo(ref writer);
                writer.Write(_bytesEndHeaders);

                // Body
                writer.Write(jsonPayload.Array, jsonPayload.Offset, jsonPayload.Count);
            }

            private void PlainText(ref WritableBuffer outputBuffer)
            {
                var writer = new WritableBufferWriter(outputBuffer);

                // HTTP 1.1 OK
                writer.Write(_bytesHttpVersion11);
                writer.Write(ReasonPhrases.ToStatusBytes(200, reasonPhrase: null));

                // Headers
                var values = _dateHeaderValueManager.GetDateHeaderValues();
                ResponseHeaders.SetRawDate(values.String, values.Bytes);
                ResponseHeaders.SetRawServer("Custom", _headerServer);
                ResponseHeaders.HeaderContentType = "text/plain";
                ResponseHeaders.ContentLength = _plainTextBody.Length;

                // Write headers
                ResponseHeaders.CopyTo(ref writer);
                writer.Write(_bytesEndHeaders);

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


        public class IPEndPointInformation : IEndPointInformation
        {
            public IPEndPointInformation(System.Net.IPEndPoint endPoint)
            {
                IPEndPoint = endPoint;
            }

            public ListenType Type => ListenType.IPEndPoint;

            public System.Net.IPEndPoint IPEndPoint { get; set; }

            public string SocketPath => null;

            public ulong FileHandle => 0;

            public bool NoDelay { get; set; } = true;

            public FileHandleType HandleType { get; set; } = FileHandleType.Tcp;

            public override string ToString()
            {
                return IPEndPoint?.ToString();
            }
        }
    }
}
