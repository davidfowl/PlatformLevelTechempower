using System;
using System.Buffers;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Protocols
{
    public static class EchoApplicationConnectionBuilderExtensions
    {
        public static IConnectionBuilder UseEchoServer(this IConnectionBuilder builder)
        {
            return builder.Use(next => new EchoApplication().ExecuteAsync);
        }
    }

    public class EchoApplication
    {
        public async Task ExecuteAsync(ConnectionContext connection)
        {
            try
            {
                while (true)
                {
                    var result = await connection.Transport.Input.ReadAsync();
                    var buffer = result.Buffer;

                    if (!buffer.IsEmpty)
                    {
                        foreach (var memory in buffer)
                        {
                            connection.Transport.Output.Write(memory.Span);
                        }

                        await connection.Transport.Output.FlushAsync();
                    }
                    else if (result.IsCompleted)
                    {
                        break;
                    }

                    connection.Transport.Input.AdvanceTo(buffer.End);
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
    }
}
