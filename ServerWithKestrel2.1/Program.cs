using System;
using System.Net;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Protocols;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

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
                .UseKestrel(o =>
                {
                    // This end point isn't using ASP.NET Core but a custom HttpServer
                    // built on top of Kestrel's transport layer
                    o.Listen(IPAddress.Loopback, 8080, builder =>
                    {
                        builder.Use(HttpServer);
                    });
                })
                .UseStartup<Startup>()
                .Build();

        public static ConnectionDelegate HttpServer(ConnectionDelegate next)
        {
            // We're ignoring next because we don't want to call into the default
            // HttpServer implementation
            var application = new PlainTextRawApplication();
            return application.ExecuteAsync;
        }
    }
}
