using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HttpMethod = Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpMethod;
using HttpVersion = Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpVersion;

namespace ServerWithKestrel21
{
    public class ProxyConnection : HttpConnection
    {
        private static HttpClient _client = new HttpClient();
        private HttpRequestMessage _requestMessage;

        private static AsciiString _crlf = "\r\n";
        private static AsciiString _http11 = "HTTP/1.1 ";

        private readonly StreamWrapper _output;

        public ProxyConnection()
        {
            _output = new StreamWrapper(this);
        }

        public override void OnStartLine(HttpMethod method, HttpVersion version, Span<byte> target, Span<byte> path, Span<byte> query, Span<byte> customMethod, bool pathEncoded)
        {
            _requestMessage = new HttpRequestMessage
            {
                RequestUri = new Uri("http://www.bing.com" + Encoding.ASCII.GetString(target)),
                Method = GetMethod(method, customMethod),
            };
        }

        // REVIEW: This doesn't handle multi header values
        public override void OnHeader(Span<byte> name, Span<byte> value)
        {
            var nameString = Encoding.ASCII.GetString(name);

            if (name.SequenceEqual((AsciiString)"Host"))
            {
                return;
            }

            var valueString = Encoding.UTF8.GetString(value);

            if (!_requestMessage.Headers.TryAddWithoutValidation(nameString, valueString))
            {
                _requestMessage.Content.Headers.TryAddWithoutValidation(nameString, valueString);
            }
        }

        public override async Task ProcessRequestAsync()
        {
            var response = await _client.SendAsync(_requestMessage, HttpCompletionOption.ResponseHeadersRead);

            WriteStartLineAndHeaders(response);

            await Connection.Transport.Output.FlushAsync();

            // Write the response body through a stream wrapper
            await response.Content.CopyToAsync(_output);
        }

        private unsafe void WriteStartLineAndHeaders(HttpResponseMessage response)
        {
            var writer = new BufferWriter<PipeWriter>(Connection.Transport.Output);

            byte space = (byte)' ';
            byte colon = (byte)':';
            byte comma = (byte)',';

            // HTTP/1.1
            writer.Write(_http11);
            writer.Write(new Span<byte>(&space, 1));

            // 200
            writer.WriteNumeric((ulong)response.StatusCode);
            writer.Write(new Span<byte>(&space, 1));

            // OK
            writer.Write(GetStatusCode(response.StatusCode));
            writer.Write(_crlf);

            foreach (var header in response.Headers)
            {
                // key
                WriteAsciiString(ref writer, header.Key);

                // :
                writer.Write(new Span<byte>(&colon, 1));
                writer.Write(new Span<byte>(&space, 1));

                // value
                bool first = true;
                foreach (var value in header.Value)
                {
                    if (!first)
                    {
                        writer.Write(new Span<byte>(&comma, 1));
                    }

                    WriteAsciiString(ref writer, value);
                    first = false;
                }

                // End of header
                writer.Write(_crlf);
            }

            // End of headers
            writer.Write(_crlf);

            writer.Commit();
        }

        private static unsafe void WriteAsciiString(ref BufferWriter<PipeWriter> writer, string value)
        {
            var bytes = Encoding.ASCII.GetByteCount(value);
            writer.Ensure(bytes);
            Encoding.ASCII.GetBytes(value.AsSpan(), writer.Span);
            writer.Advance(bytes);
        }

        private ReadOnlySpan<byte> GetStatusCode(HttpStatusCode statusCode)
        {
            return Encoding.ASCII.GetBytes(statusCode.ToString());
        }

        private System.Net.Http.HttpMethod GetMethod(HttpMethod method, Span<byte> customMethod)
        {
            switch (method)
            {
                case HttpMethod.Get:
                    return System.Net.Http.HttpMethod.Get;
                case HttpMethod.Put:
                    return System.Net.Http.HttpMethod.Put;
                case HttpMethod.Delete:
                    return System.Net.Http.HttpMethod.Delete;
                case HttpMethod.Post:
                    return System.Net.Http.HttpMethod.Post;
                case HttpMethod.Head:
                    return System.Net.Http.HttpMethod.Head;
                case HttpMethod.Trace:
                    return System.Net.Http.HttpMethod.Trace;
                case HttpMethod.Patch:
                    return System.Net.Http.HttpMethod.Patch;
                case HttpMethod.Connect:
                    return new System.Net.Http.HttpMethod("CONNECT");
                case HttpMethod.Options:
                    return System.Net.Http.HttpMethod.Options;
                case HttpMethod.Custom:
                    break;
                case HttpMethod.None:
                    break;
                default:
                    break;
            }
            return new System.Net.Http.HttpMethod(Encoding.ASCII.GetString(customMethod));
        }

        private class StreamWrapper : Stream
        {
            private readonly ProxyConnection _connection;

            public StreamWrapper(ProxyConnection connection)
            {
                _connection = connection;
            }

            public override bool CanRead => throw new NotImplementedException();

            public override bool CanSeek => throw new NotImplementedException();

            public override bool CanWrite => true;

            public override long Length => throw new NotImplementedException();

            public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override async Task WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
            {
                await _connection.Connection.Transport.Output.WriteAsync(source, cancellationToken);
            }

            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                await _connection.Connection.Transport.Output.WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken);
            }
        }
    }
}
