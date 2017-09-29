using System;

namespace PlatformLevelTechempower
{
    public class Args
    {
        private Args() { }

        public Mode Mode { get; set; } = Mode.Raw;

        public int Port { get; set; } = 8081;

        public int ThreadCount { get; set; } = Environment.ProcessorCount;

        public static Args Parse(string[] args)
        {
            var namePrefix = "--";
            var result = new Args();

            for (int i = 0; i < args.Length; i++)
            {
                var name = args[i];
                if (string.Equals(namePrefix + nameof(Mode), name, StringComparison.OrdinalIgnoreCase))
                {
                    var value = args[i + 1];
                    if (Enum.TryParse(value, out Mode mode))
                    {
                        result.Mode = mode;
                    }
                    i++;
                    continue;
                }
                if (string.Equals(namePrefix + nameof(Port), name, StringComparison.OrdinalIgnoreCase))
                {
                    var value = args[i + 1];
                    if (int.TryParse(value, out int port))
                    {
                        result.Port = port;
                    }
                    i++;
                    continue;
                }
                if (string.Equals(namePrefix + nameof(ThreadCount), name, StringComparison.OrdinalIgnoreCase))
                {
                    var value = args[i + 1];
                    if (int.TryParse(value, out int threadCount))
                    {
                        result.ThreadCount = threadCount;
                    }
                    i++;
                    continue;
                }
            }

            return result;
        }
    }

    public enum Mode
    {
        Raw,
        RawWithHeaders,
        HttpServer,
        Features
    }
}
