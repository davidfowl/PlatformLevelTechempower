using System.Net;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;

namespace ServerWithKestrel21
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BuildWebHost(args).Run();
        }

        public static IWebHost BuildWebHost(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseKestrel(options =>
                {
                    options.Listen(IPAddress.Loopback, 8080, builder =>
                    {
                        builder.UseHttpApplication<PlainTextConnection>();
                    });
                })
                .UseStartup<Startup>()
                .Build();
    }
}
