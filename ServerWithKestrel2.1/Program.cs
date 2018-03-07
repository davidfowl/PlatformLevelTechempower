using System.Net;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Protocols;

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
                    options.Listen(IPAddress.Any, 6005, builder =>
                    {
                        builder.UseEchoServer();
                    });

                    options.Listen(IPAddress.Loopback, 8080, builder =>
                    {
                        builder.UseHttpApplication<PlainTextConnection>();
                    });
                })
                .UseStartup<Startup>()
                .Build();
    }
}
