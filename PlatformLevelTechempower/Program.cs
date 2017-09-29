using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PlatformLevelTechempower
{
    public partial class Program
    {
        public static Task Main(string[] args)
        {
            var parsedArgs = Args.Parse(args);

            IServerApplication app;

            if (parsedArgs.Mode == Mode.Raw)
            {
                app = new PlainTextRawApplication();
            }
            else if (parsedArgs.Mode == Mode.Features)
            {
                app = new PlainTextApplication();
            }
            else if (parsedArgs.Mode == Mode.RawWithHeaders)
            {
                app = new PlainTextRawWithHeadersApplication();
            }
            else
            {
                app = new HttpServer<BenchmarkHandler>();
            }

            return app.RunAsync(parsedArgs.Port, parsedArgs.ThreadCount);
        }
    }
}
