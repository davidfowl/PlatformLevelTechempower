using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PlatformLevelTechempower
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            var parsedArgs = ParseArgs(args);

            IServerApplication app;

            if (parsedArgs.Raw)
            {
                app = new PlainTextRawApplication();
            }
            else
            {
                app = new PlainTextApplication();
            }

            return app.RunAsync(parsedArgs.Port);
        }

        private static Args ParseArgs(string[] args)
        {
            var namePrefix = "--";
            var result = new Args();

            for (int i = 0; i < args.Length; i++)
            {
                var name = args[i];
                if (string.Equals(namePrefix + nameof(Args.Raw), name, StringComparison.OrdinalIgnoreCase))
                {
                    var value = args[i + 1];
                    if (bool.TryParse(value, out bool raw))
                    {
                        result.Raw = raw;
                    }
                    i++;
                    continue;
                }
                if (string.Equals(namePrefix + nameof(Args.Port), name, StringComparison.OrdinalIgnoreCase))
                {
                    var value = args[i + 1];
                    if (int.TryParse(value, out int port))
                    {
                        result.Port = port;
                    }
                    i++;
                    continue;
                }
            }

            return result;
        }

        private class Args
        {
            public bool Raw { get; set; }

            public int Port { get; set; } = 8081;
        }
    }
}
