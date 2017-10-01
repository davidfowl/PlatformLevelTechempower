using BenchmarkDotNet.Attributes;

namespace PlatformLevelTechempower
{
    public class HttpServerBenchmark
    {
        private const int _iterations = 10000;

        public HttpServerBenchmark()
        {
            
        }

        [Benchmark]
        public void Plaintext()
        {
            // TODO: Do something here
        }
    }
}