using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using System.Net;

namespace PlatformLevelTechempower
{
    public class IPEndPointInformation : IEndPointInformation
    {
        public IPEndPointInformation(IPEndPoint endPoint)
        {
            IPEndPoint = endPoint;
        }

        public ListenType Type => ListenType.IPEndPoint;

        public IPEndPoint IPEndPoint { get; set; }

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
