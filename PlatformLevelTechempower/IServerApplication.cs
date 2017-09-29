using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PlatformLevelTechempower
{
    public interface IServerApplication
    {
        Task RunAsync(int port, int threadCount);
    }
}
