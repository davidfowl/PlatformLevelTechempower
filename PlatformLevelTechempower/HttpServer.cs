using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.Net.Http.Headers;

namespace PlatformLevelTechempower
{
    public class HttpServer<THandler> : IConnectionHandler, IServerApplication where THandler : Handler, new()
    {
        private static readonly byte[] _headerConnection = Encoding.ASCII.GetBytes("Connection");
        private static readonly byte[] _headerConnectionKeepAlive = Encoding.ASCII.GetBytes("keep-alive");

        public static readonly int DefaultThreadCount = Environment.ProcessorCount;

        public Task RunAsync(int port) => RunAsync(port, DefaultThreadCount);

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

            Console.WriteLine($"Server listening on http://*:{port} with {libuvOptions.ThreadCount} thread(s)");

            lifetime.ApplicationStopping.WaitHandle.WaitOne();

            await transport.UnbindAsync();
            await transport.StopAsync();
        }

        IConnectionContext IConnectionHandler.OnConnection(IConnectionInformation connectionInfo)
        {
            var inputOptions = new PipeOptions { WriterScheduler = connectionInfo.InputWriterScheduler };
            var outputOptions = new PipeOptions { ReaderScheduler = connectionInfo.OutputReaderScheduler };

            var context = new HttpConnectionContext<THandler>()
            {
                ConnectionId = Guid.NewGuid().ToString(),
                Input = connectionInfo.PipeFactory.Create(inputOptions),
                Output = connectionInfo.PipeFactory.Create(outputOptions)
            };

            _ = context.ExecuteAsync();

            return context;
        }

        private class HttpConnectionContext<THandlerInner> : IConnectionContext, IHttpHeadersHandler, IHttpRequestLineHandler
            where THandlerInner : Handler, new()
        {
            private readonly Handler _handler;

            private State _state;

            public HttpConnectionContext()
            {
                _handler = new THandlerInner();
            }

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
                    var parser = new HttpParser<HttpConnectionContext<THandlerInner>>();

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

                            ParseHttpRequest(parser, inputBuffer, out consumed, out examined);

                            if (_state != State.Body && result.IsCompleted)
                            {
                                // Bad request
                                break;
                            }

                            if (_state == State.Body)
                            {
                                var outputBuffer = Output.Writer.Alloc();

                                _handler.Output = new WritableBufferWriter(outputBuffer);

                                await _handler.ProcessAsync();

                                await outputBuffer.FlushAsync();

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

            private void ParseHttpRequest(HttpParser<HttpConnectionContext<THandlerInner>> parser, ReadableBuffer inputBuffer, out ReadCursor consumed, out ReadCursor examined)
            {
                consumed = inputBuffer.Start;
                examined = inputBuffer.End;

                if (_state == State.StartLine)
                {
                    if (parser.ParseRequestLine(this, inputBuffer, out consumed, out examined))
                    {
                        _state = State.Headers;
                        inputBuffer = inputBuffer.Slice(consumed);
                    }
                }

                if (_state == State.Headers)
                {
                    if (parser.ParseHeaders(this, inputBuffer, out consumed, out examined, out int consumedBytes))
                    {
                        _state = State.Body;
                    }
                }
            }

            public void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
            {
                _handler.Method = method;

                Array.Clear(_handler.Path, 0, _handler.Path.Length);
                path.CopyTo(_handler.Path);
                _handler.PathLength = path.Length;

                Array.Clear(_handler.Query, 0, _handler.Query.Length);
                query.CopyTo(_handler.Query);
                _handler.QueryLength = query.Length;
            }

            public void OnHeader(Span<byte> name, Span<byte> value)
            {
                if (name.SequenceEqual(_headerConnection) && value.SequenceEqual(_headerConnectionKeepAlive))
                {
                    _handler.KeepAlive = true;
                }

                _handler.OnHeader(name, value);
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

    public abstract class Handler
    {
        private static readonly byte[] _crlf = Encoding.ASCII.GetBytes("\r\n");
        private static readonly byte[] _http11StartLine = Encoding.ASCII.GetBytes("HTTP/1.1 ");

        private static readonly byte[] _headerServer = Encoding.ASCII.GetBytes("Server: Kestrel");
        private static readonly byte[] _headerContentLength = Encoding.ASCII.GetBytes("Content-Length: ");
        private static readonly byte[] _headerContentType = Encoding.ASCII.GetBytes("Content-Type: ");
        private static readonly byte[] _headerContentLengthZero = Encoding.ASCII.GetBytes("0");
        private static readonly byte[] _headerConnectionKeepAlive = Encoding.ASCII.GetBytes("Connection: keep-alive");

        private static readonly DateHeaderValueManager _dateHeaderValueManager = new DateHeaderValueManager();

        public HttpMethod Method { get; set; }

        public byte[] Path { get; set; } = new byte[256];

        public int PathLength { get; set; }

        public byte[] Query { get; set; } = new byte[256];

        public int QueryLength { get; set; }

        public bool KeepAlive { get; set; }

        public WritableBufferWriter Output { get; set; }

        public virtual void OnHeader(Span<byte> name, Span<byte> value)
        {

        }

        public abstract Task ProcessAsync();

        public bool PathMatch(byte[] path, byte[] target)
        {
            var pathSpan = path.AsSpan().Slice(0, PathLength);
            return pathSpan.SequenceEqual(target);
        }

        public void Ok(byte[] body, MediaType mediaType)
        {
            WriteStartLine(HttpStatus.Ok);

            WriteCommonHeaders();

            WriteHeader(_headerContentType, mediaType.Value);
            WriteHeader(_headerContentLength, (ulong)body.Length);

            Output.Write(_crlf);
            Output.Write(body);
        }

        public void Json<T>(T value)
        {
            WriteStartLine(HttpStatus.Ok);

            WriteCommonHeaders();

            WriteHeader(_headerContentType, MediaType.ApplicationJson.Value);

            var body = JsonSerializer.Serialize(value);

            WriteHeader(_headerContentLength, (ulong)body.Length);

            Output.Write(_crlf);
            Output.Write(body);
        }

        public void NotFound()
        {
            WriteResponse(HttpStatus.NotFound);
        }

        public void BadRequest()
        {
            WriteResponse(HttpStatus.BadRequest);
        }

        public void WriteHeader(byte[] name, ulong value)
        {
            var output = Output;
            output.Write(name);
            PipelineExtensions.WriteNumeric(ref output, value);
            output.Write(_crlf);
        }

        public void WriteHeader(byte[] name, byte[] value)
        {
            Output.Write(name);
            Output.Write(value);
            Output.Write(_crlf);
        }

        private void WriteResponse(HttpStatus status)
        {
            WriteStartLine(status);
            WriteCommonHeaders();
            WriteHeader(_headerContentLength, 0);
            Output.Write(_crlf);
        }

        private void WriteStartLine(HttpStatus status)
        {
            Output.Write(_http11StartLine);
            Output.Write(status.Value);
            Output.Write(_crlf);
        }

        private void WriteCommonHeaders()
        {
            // Server headers
            Output.Write(_headerServer);

            // Date header
            Output.Write(_dateHeaderValueManager.GetDateHeaderValues().Bytes);
            Output.Write(_crlf);

            if (KeepAlive)
            {
                Output.Write(_headerConnectionKeepAlive);
                Output.Write(_crlf);
            }
        }
    }

    public struct HttpStatus
    {
        public static HttpStatus Ok = new HttpStatus(200, "OK");
        public static HttpStatus BadRequest = new HttpStatus(400, "BAD REQUEST");
        public static HttpStatus NotFound = new HttpStatus(404, "NOT FOUND");
        // etc.

        private readonly byte[] _value;

        private HttpStatus(int code, string message)
        {
            _value = Encoding.ASCII.GetBytes(code.ToString() + " " + message);
        }

        public byte[] Value => _value;
    }

    public struct MediaType
    {
        public static MediaType TextPlain = new MediaType("text/plain");
        public static MediaType ApplicationJson = new MediaType("application/json");
        // etc.

        private readonly byte[] _value;

        private MediaType(string value)
        {
            _value = Encoding.ASCII.GetBytes(value);
        }

        public byte[] Value => _value;
    }
}
