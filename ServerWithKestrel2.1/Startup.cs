using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Net.Http.Headers;

namespace ServerWithKestrel21
{
    public class Startup
    {
        private static AsciiString _helloWorldPayload = "Hello, World!";
        private static readonly int _helloWorldLength = _helloWorldPayload.Length;
        private static readonly string _helloWorldLengthValue = _helloWorldPayload.Length.ToString();

        public void Configure(IApplicationBuilder app) 
        {
            // This is the ASP.NET Core application running on Kestrel
            app.Run(context =>
            {
                context.Response.StatusCode = 200;
                context.Response.Headers[HeaderNames.ContentType] = "text/plain";
                context.Response.Headers[HeaderNames.ContentLength] = _helloWorldLengthValue;
                return context.Response.Body.WriteAsync(_helloWorldPayload, 0, _helloWorldLength);
            });
        }
    }
}
