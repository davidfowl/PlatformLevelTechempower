using System;
using System.Buffers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Protocols;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using HttpVersion = Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpVersion;

namespace ServerWithKestrel21
{
    public static class HttpApplicationConnectionBuilderExtensions
    {
        public static IConnectionBuilder UseHttpApplication<TConnection>(this IConnectionBuilder builder) where TConnection : HttpConnection, new()
        {
            return builder.Use(next => new HttpApplication<TConnection>().ExecuteAsync);
        }
    }

    public class HttpApplication<TConnection> where TConnection : HttpConnection, new()
    {
        public Task ExecuteAsync(ConnectionContext connection)
        {
            var parser = new HttpParser<HttpConnection>();

            var httpConnection = new TConnection
            {
                Connection = connection,
                Parser = parser
            };
            return httpConnection.ExecuteAsync();
        }
    }

    public class HttpConnection : IHttpHeadersHandler, IHttpRequestLineHandler
    {
        private State _state;

        public ConnectionContext Connection { get; set; }

        internal HttpParser<HttpConnection> Parser { get; set; }

        public virtual void OnHeader(Span<byte> name, Span<byte> value)
        {
            
        }

        public virtual void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
        {

        }

        public virtual Task ProcessRequestAsync()
        {
            return Task.CompletedTask;
        }

        public async Task ExecuteAsync()
        {
            while (true)
            {
                try
                {
                    while (true)
                    {
                        var result = await Connection.Transport.Input.ReadAsync();
                        var buffer = result.Buffer;
                        var consumed = buffer.Start;
                        var examined = buffer.End;

                        try
                        {
                            if (!buffer.IsEmpty)
                            {
                                ParseHttpRequest(buffer, out consumed, out examined);

                                if (_state == State.Body)
                                {
                                    await ProcessRequestAsync();

                                    // After processing the request, change the state to start line
                                    // This means the application is responsible for handling body since we assume by the time this returns
                                    // that the application has processed the entire request

                                    _state = State.StartLine;
                                }
                                else if (result.IsCompleted)
                                {
                                    throw new InvalidOperationException("Unexpected end of data!");
                                }
                            }
                            else if (result.IsCompleted)
                            {
                                break;
                            }
                        }
                        finally
                        {
                            Connection.Transport.Input.AdvanceTo(consumed, examined);
                        }
                    }

                    Connection.Transport.Input.Complete();
                }
                catch (Exception ex)
                {
                    Connection.Transport.Input.Complete(ex);
                }
                finally
                {
                    Connection.Transport.Output.Complete();
                }
            }
        }

        private void ParseHttpRequest(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
        {
            consumed = buffer.Start;
            examined = buffer.End;

            if (_state == State.StartLine)
            {
                if (Parser.ParseRequestLine(this, buffer, out consumed, out examined))
                {
                    _state = State.Headers;
                    buffer = buffer.Slice(consumed);
                }
            }

            if (_state == State.Headers)
            {
                if (Parser.ParseHeaders(this, buffer, out consumed, out examined, out int consumedBytes))
                {
                    _state = State.Body;
                }
            }
        }

        private enum State
        {
            StartLine,
            Headers,
            Body
        }
    }
}
