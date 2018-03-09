using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace ServerWithKestrel21
{
    internal class DuplexStream : Stream
    {
        private readonly IDuplexPipe _transport;

        public DuplexStream(IDuplexPipe transport)
        {
            _transport = transport;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new System.NotSupportedException();

        public override long Position { get => throw new System.NotSupportedException(); set => throw new System.NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new System.NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new System.NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new System.NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
        }
        
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
        {
            await _transport.Output.WriteAsync(source, cancellationToken);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            while (true)
            {
                var result = await _transport.Input.ReadAsync();
                var readableBuffer = result.Buffer;
                try
                {
                    if (!readableBuffer.IsEmpty)
                    {
                        // buffer.Count is int
                        var count = (int)Math.Min(readableBuffer.Length, destination.Length);
                        readableBuffer = readableBuffer.Slice(0, count);
                        readableBuffer.CopyTo(destination.Span);
                        return count;
                    }
                    else if (result.IsCompleted)
                    {
                        return 0;
                    }
                }
                finally
                {
                    _transport.Input.AdvanceTo(readableBuffer.End, readableBuffer.End);
                }
            }
        }
    }
}