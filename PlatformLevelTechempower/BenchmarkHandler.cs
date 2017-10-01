using System.Threading.Tasks;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;

namespace PlatformLevelTechempower
{
    public sealed class BenchmarkHandler : HttpHandler
    {
        private static AsciiString _plainTextBody = "Hello, World!";

        private static class Paths
        {
            public static AsciiString Plaintext = "/plaintext";
            public static AsciiString Json = "/json";
        }

        public override Task ProcessAsync()
        {
            if (Method == HttpMethod.Get)
            {
                if (PathMatch(Paths.Plaintext))
                {
                    Ok(_plainTextBody, MediaType.TextPlain);
                    return Task.CompletedTask;
                }
                else if (PathMatch(Paths.Json))
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
