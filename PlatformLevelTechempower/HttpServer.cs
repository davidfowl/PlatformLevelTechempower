using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System;
using Microsoft.AspNetCore.Server.Kestrel.Internal.System.IO.Pipelines;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Utf8Json;

namespace PlatformLevelTechempower
{
    public class HttpServer<THandler> : IConnectionHandler, IServerApplication
        where THandler : HttpHandler, new()
    {
        private static AsciiString _headerConnection = "Connection";
        private static AsciiString _headerConnectionKeepAlive = "keep-alive";

        public static readonly int DefaultThreadCount = Environment.ProcessorCount;

        public async Task RunAsync(ITransportFactory transportFactory, IEndPointInformation endPointInformation, ApplicationLifetime lifetime)
        {
            Console.CancelKeyPress += (sender, e) => lifetime.StopApplication();

            var transport = transportFactory.Create(endPointInformation, this);

            await transport.BindAsync();

            Console.WriteLine($"Server ({nameof(HttpServer<THandler>)}) listening on http://{endPointInformation.IPEndPoint}");

            lifetime.ApplicationStopping.WaitHandle.WaitOne();

            await transport.UnbindAsync();
            await transport.StopAsync();
        }

        IConnectionContext IConnectionHandler.OnConnection(IConnectionInformation connectionInfo)
        {
            var inputOptions = new PipeOptions { WriterScheduler = connectionInfo.InputWriterScheduler };
            var outputOptions = new PipeOptions { ReaderScheduler = connectionInfo.OutputReaderScheduler };

            var context = new HttpConnectionContext<THandler>
            {
                ConnectionId = Guid.NewGuid().ToString(),
                Input = connectionInfo.PipeFactory.Create(inputOptions),
                Output = connectionInfo.PipeFactory.Create(outputOptions)
            };

            _ = context.ExecuteAsync();

            return context;
        }

        private class HttpConnectionContext<THandlerInner> : IConnectionContext, IHttpHeadersHandler, IHttpRequestLineHandler
            where THandlerInner : HttpHandler, new()
        {
            private static readonly HttpParser<HttpConnectionContext<THandlerInner>> _parser = new HttpParser<HttpConnectionContext<THandlerInner>>();

            private THandlerInner _handler;

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
                _handler = null;
            }

            public void OnConnectionClosed(Exception ex)
            {
                _handler = null;
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

                                _handler.Output = outputBuffer;

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
                if (version != HttpVersion.Http11)
                {
                    throw new Exception("Only HTTP 1.1 is supported");
                }

                _handler.HandleStartLine(method, version, target, path, query, customMethod, pathEncoded);
            }

            public void OnHeader(Span<byte> name, Span<byte> value)
            {
                //if (name.SequenceEqual(_headerConnection) && value.SequenceEqual(_headerConnectionKeepAlive))
                //{
                //    _handler.KeepAlive = true;
                //}

                //_handler.OnHeader(name, value);
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

    public abstract class HttpHandler
    {
        private static AsciiString _crlf = "\r\n";
        private static AsciiString _http11StartLine = "HTTP/1.1 ";

        private static AsciiString _headerServer = "Server: Kestrel";
        private static AsciiString _headerContentLength = "Content-Length: ";
        private static AsciiString _headerContentType = "Content-Type: ";
        private static AsciiString _headerConnectionKeepAlive = "Connection: keep-alive";

        private static readonly DateHeaderValueManager _dateHeaderValueManager = new DateHeaderValueManager();

        private readonly byte[] _pathFixedBuffer = new byte[128];
        private byte[] _pathLargeBuffer;
        private int _pathLength;

        private readonly byte[] _queryFixedBuffer = new byte[128];
        private byte[] _queryLargeBuffer;
        private int _queryLength;

        public HttpMethod Method { get; set; }

        public Span<byte> Path => _pathLargeBuffer != null ? _pathLargeBuffer.AsSpan() : new Span<byte>(_pathFixedBuffer, 0, _pathLength);

        public Span<byte> Query => _queryLargeBuffer != null ? _queryLargeBuffer.AsSpan() : new Span<byte>(_queryFixedBuffer, 0, _queryLength);

        public bool KeepAlive { get; set; }

        internal WritableBuffer Output { get; set; }

        internal void HandleStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
        {
            Method = method;

            if (path.TryCopyTo(_pathFixedBuffer))
            {
                _pathLength = path.Length;
            }
            else // path > 128
            {
                _pathLargeBuffer = path.ToArray();
            }

            if (query.TryCopyTo(_queryFixedBuffer))
            {
                _queryLength = query.Length;
            }
            else // query > 128
            {
                _queryLargeBuffer = query.ToArray();
            }

            OnStartLine(method, version, target, path, query, customMethod, pathEncoded);
        }

        public virtual void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
        {

        }

        public virtual void OnHeader(Span<byte> name, Span<byte> value)
        {

        }

        public abstract Task ProcessAsync();

        public bool PathMatch(AsciiString target)
        {
            return Path.SequenceEqual(target);
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

            var body = JsonSerializer.SerializeUnsafe(value);
            WriteHeader(_headerContentLength, (ulong)body.Count);

            Output.Write(_crlf);
            Output.Write(body.Array, body.Offset, body.Count);
        }

        public void NotFound()
        {
            WriteResponse(HttpStatus.NotFound);
        }

        public void BadRequest()
        {
            WriteResponse(HttpStatus.BadRequest);
        }

        public void WriteHeader(AsciiString name, ulong value)
        {
            Output.Write(name);
            var output = new WritableBufferWriter(Output);
            PipelineExtensions.WriteNumeric(ref output, value);
            Output.Write(_crlf);
        }

        public void WriteHeader(AsciiString name, AsciiString value)
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

        private AsciiString _value;

        private HttpStatus(int code, string message)
        {
            _value = code.ToString() + " " + message;
        }

        public AsciiString Value => _value;
    }

    public struct MediaType
    {
        public static MediaType TextPlain = new MediaType("text/plain");
        public static MediaType ApplicationJson = new MediaType("application/json");
        // etc.

        private AsciiString _value;

        private MediaType(string value)
        {
            _value = value;
        }

        public AsciiString Value => _value;
    }
}
