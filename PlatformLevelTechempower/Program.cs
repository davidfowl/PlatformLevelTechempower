using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PlatformLevelTechempower
{
    public class Program
    {
        public static Task Main()
        {
            var app = new PlainTextApplication();
            return app.RunAsync(8081);
        }
    }
}
