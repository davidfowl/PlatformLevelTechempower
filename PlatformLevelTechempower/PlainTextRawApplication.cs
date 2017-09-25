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

namespace PlatformLevelTechempower
{
    public class PlainTextRawApplication : IConnectionHandler
    {
        private static readonly byte[] _plainTextResposne = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 13\r\nContent-Type: text/plain\r\n\r\nHello, World!");

        public async Task RunAsync(int port)
        {
            var lifetime = new ApplicationLifetime(NullLoggerFactory.Instance.CreateLogger<ApplicationLifetime>());

            Console.CancelKeyPress += (sender, e) => lifetime.StopApplication();

            var libuvOptions = new LibuvTransportOptions();
            var libuvTransport = new LibuvTransportFactory(
                Options.Create(libuvOptions),
                lifetime,
                NullLoggerFactory.Instance);

            var binding = new IPEndPointInformation(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port));

            var transport = libuvTransport.Create(binding, this);
            await transport.BindAsync();

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
                Output = connectionInfo.PipeFactory.Create(outputOptions)
            };

            _ = context.ExecuteAsync();

            return context;
        }

        private class HttpConnectionContext : IConnectionContext, IHttpHeadersHandler, IHttpRequestLineHandler
        {
            private State _state;

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
                    var parser = new HttpParser<HttpConnectionContext>();

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
                                outputBuffer.Write(_plainTextResposne);
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

            private void ParseHttpRequest(HttpParser<HttpConnectionContext> parser, ReadableBuffer inputBuffer, out ReadCursor consumed, out ReadCursor examined)
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
