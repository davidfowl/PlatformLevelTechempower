using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Libuv;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace PlatformLevelTechempower
{
    public class PlainTextApplication : IHttpApplication<IFeatureCollection>, IServerApplication
    {
        private static readonly byte[] _helloWorldPayload = Encoding.UTF8.GetBytes("Hello, World!");
        private static readonly int _helloWorldLength = _helloWorldPayload.Length;
        private static readonly string _helloWorldLengthValue = _helloWorldPayload.Length.ToString();

        public async Task RunAsync(int port, int threadCount)
        {
            var lifetime = new ApplicationLifetime(NullLoggerFactory.Instance.CreateLogger<ApplicationLifetime>());

            Console.CancelKeyPress += (sender, e) => lifetime.StopApplication();

            var libuvOptions = new LibuvTransportOptions
            {
                ThreadCount = threadCount
            };
            var libuvTransport = new LibuvTransportFactory(
                Options.Create(libuvOptions),
                lifetime,
                NullLoggerFactory.Instance);

            var serverOptions = new KestrelServerOptions();
            serverOptions.Listen(IPAddress.Any, port);

            var server = new KestrelServer(Options.Create(serverOptions),
                                           libuvTransport,
                                           NullLoggerFactory.Instance);

            await server.StartAsync(this, CancellationToken.None);

            Console.WriteLine($"Server listening on http://*:{port}");

            lifetime.ApplicationStopping.WaitHandle.WaitOne();

            await server.StopAsync(CancellationToken.None);
        }

        public IFeatureCollection CreateContext(IFeatureCollection contextFeatures)
        {
            return contextFeatures;
        }

        public void DisposeContext(IFeatureCollection context, Exception exception)
        {

        }

        public Task ProcessRequestAsync(IFeatureCollection context)
        {
            var resonseFeature = context.Get<IHttpResponseFeature>();

            resonseFeature.StatusCode = 200;
            resonseFeature.Headers[HeaderNames.ContentType] = "text/plain";
            resonseFeature.Headers[HeaderNames.ContentLength] = _helloWorldLengthValue;
            return resonseFeature.Body.WriteAsync(_helloWorldPayload, 0, _helloWorldLength);
        }
    }
}
