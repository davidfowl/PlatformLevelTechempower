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
using Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.Internal;

namespace PlatformLevelTechempower
{
    public class PlainTextApplication : IHttpApplication<IFeatureCollection>, IServerApplication
    {
        private static readonly byte[] _helloWorldPayload = Encoding.UTF8.GetBytes("Hello, World!");
        private static readonly int _helloWorldLength = _helloWorldPayload.Length;
        private static readonly string _helloWorldLengthValue = _helloWorldPayload.Length.ToString();

        public async Task RunAsync(ITransportFactory transportFactory, IEndPointInformation endPointInformation, ApplicationLifetime lifetime)
        {
            Console.CancelKeyPress += (sender, e) => lifetime.StopApplication();

            var serverOptions = new KestrelServerOptions();
            serverOptions.Listen(endPointInformation.IPEndPoint);

            var server = new KestrelServer(Options.Create(serverOptions),
                                           transportFactory,
                                           NullLoggerFactory.Instance);

            await server.StartAsync(this, CancellationToken.None);

            Console.WriteLine($"Server ({nameof(PlainTextApplication)}) listening on http://{endPointInformation.IPEndPoint}");

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
