using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace PlatformLevelTechempower
{
    public class BenchmarkHandler : Handler
    {
        private static readonly byte[] _plainTextBody = Encoding.UTF8.GetBytes("Hello, World!");

        private static class Paths
        {
            public static readonly byte[] Plaintext = Encoding.ASCII.GetBytes("/plaintext");
            public static readonly byte[] Json = Encoding.ASCII.GetBytes("/json");
        }

        public override Task ProcessAsync()
        {
            if (Method == HttpMethod.Get)
            {
                if (PathMatch(Path, Paths.Plaintext))
                {
                    Ok(_plainTextBody, MediaType.TextPlain);

                    return Task.CompletedTask;
                }
                else if (PathMatch(Path, Paths.Json))
                {
                    Json(new { message = "Hello, World!" });
                    return Task.CompletedTask;
                }
            }

            NotFound();
            return Task.CompletedTask;
        }
    }
}
