using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using BenchmarkDotNet.Running;

namespace PlatformLevelTechempower
{
    public partial class Program
    {
        public static Task Main(string[] args)
        {
            var parsedArgs = Args.Parse(args);

            if (parsedArgs.Benchmark)
            {
                BenchmarkInHarness(parsedArgs);
                return Task.CompletedTask;
            }

            IServerApplication app = null;

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

            var lifetime = new ApplicationLifetime(NullLoggerFactory.Instance.CreateLogger<ApplicationLifetime>());
            var binding = new IPEndPointInformation(new IPEndPoint(IPAddress.Any, parsedArgs.Port));
            var transportFactory = CreateTransport(parsedArgs, lifetime);

            return app.RunAsync(transportFactory, binding, lifetime);
        }

        private static ITransportFactory CreateTransport(Args parsedArgs, ApplicationLifetime lifetime)
        {
            if (parsedArgs.Transport == Transport.Libuv)
            {
                var libuvOptions = new LibuvTransportOptions
                {
                    ThreadCount = parsedArgs.ThreadCount
                };
                
                return new LibuvTransportFactory(
                    Options.Create(libuvOptions),
                    lifetime,
                    NullLoggerFactory.Instance);
            }
            if (parsedArgs.Transport == Transport.Sockets)
            {
                // TODO: Add the sockets transport

            }

            return null;
        }

        private static void BenchmarkInHarness(Args parsedArgs)
        {
            if (parsedArgs.Mode == Mode.HttpServer)
            {
                var summary = BenchmarkRunner.Run<HttpServerBenchmark>();
            }
        }
    }
}
